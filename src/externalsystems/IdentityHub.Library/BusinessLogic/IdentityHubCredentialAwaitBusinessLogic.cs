/********************************************************************************
 * Copyright (c) 2026 Contributors to the Eclipse Foundation
 *
 * See the NOTICE file(s) distributed with this work for additional
 * information regarding copyright ownership.
 *
 * This program and the accompanying materials are made available under the
 * terms of the Apache License, Version 2.0 which is available at
 * https://www.apache.org/licenses/LICENSE-2.0.
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 * SPDX-License-Identifier: Apache-2.0
 ********************************************************************************/

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.Framework.DateTimeProvider;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Processes.Library.Enums;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Processes.ApplicationChecklist.Library;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.BusinessLogic;

/// <inheritdoc />
public class IdentityHubCredentialAwaitBusinessLogic(
    IPortalRepositories portalRepositories,
    IIdentityHubService identityHubService,
    IDateTimeProvider dateTimeProvider,
    IOptions<IdentityHubSettings> options,
    ILogger<IdentityHubCredentialAwaitBusinessLogic> logger)
    : IIdentityHubCredentialAwaitBusinessLogic
{
    private readonly IdentityHubSettings _settings = options.Value;

    /// <inheritdoc />
    public Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> AwaitBpnCredentialResponse(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken) =>
        AwaitCredentialResponse(context, ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL, ProcessStepTypeId.REQUEST_MEMBERSHIP_CREDENTIAL, "BPN", _settings.BpnCredentialType, cancellationToken);

    /// <inheritdoc />
    public Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> AwaitMembershipCredentialResponse(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken) =>
        AwaitCredentialResponse(context, ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ProcessStepTypeId.RETRIGGER_REQUEST_MEMBERSHIP_CREDENTIAL, ProcessStepTypeId.START_CLEARING_HOUSE, "Membership", _settings.MembershipCredentialType, cancellationToken);

    private async Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> AwaitCredentialResponse(
        IApplicationChecklistService.WorkerChecklistProcessStepData context,
        ProcessStepTypeId awaitStepTypeId,
        ProcessStepTypeId retriggerStepTypeId,
        ProcessStepTypeId nextStepTypeId,
        string credential,
        string configuredCredentialType,
        CancellationToken cancellationToken)
    {
        var bpn = await GetBusinessPartnerNumber(context.ApplicationId).ConfigureAwait(ConfigureAwaitOptions.None);
        var state = await identityHubService.GetCredentialRequestStateAsync(bpn, configuredCredentialType, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);

        switch (state)
        {
            // The holder has it. The callback may have been lost, or may simply not have run yet - either
            // way the Portal no longer needs it to make progress, so advance exactly as the callback would.
            case HolderCredentialRequestState.Issued:
                return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
                    ProcessStepStatusId.DONE,
                    entry => entry.ApplicationChecklistEntryStatusId = ApplicationChecklistEntryStatusId.DONE,
                    Enumerable.Repeat(nextStepTypeId, 1),
                    null,
                    true,
                    null);

            // Terminal on the holder side. The IdentityHub's DTO carries no errorDetail, so the reason is
            // only in the holder's own logs - say so rather than implying the Portal knows more.
            case HolderCredentialRequestState.Failed:
                logger.LogWarning(
                    "IdentityHub reports the {Credential} credential request for application {ApplicationId} as ERROR. The reason is not exposed by the status API; check the holder logs for the {ConfiguredCredentialType} request.",
                    credential,
                    context.ApplicationId,
                    configuredCredentialType);
                return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
                    ProcessStepStatusId.FAILED,
                    null,
                    Enumerable.Repeat(retriggerStepTypeId, 1),
                    null,
                    false,
                    $"The IdentityHub reported the {credential} credential request as failed");
        }

        var dateCreated = await GetWaitingSince(context.ApplicationId, awaitStepTypeId).ConfigureAwait(ConfigureAwaitOptions.None);
        var deadline = dateCreated.AddDays(_settings.MaxCredentialWaitTimeInDays);
        if (dateTimeProvider.OffsetNow <= deadline)
        {
            // Still in flight (CREATED / REQUESTING / REQUESTED), or not yet visible. Stay in TODO without
            // touching the checklist entry, so the callback can still finalize this step the fast way.
            return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
                ProcessStepStatusId.TODO,
                null,
                null,
                null,
                false,
                null);
        }

        // Past the deadline and the IdentityHub still does not report a terminal state, so this is not a
        // lost callback - polling would have seen ISSUED. Either the issuer accepted and never delivered
        // (the request sits in REQUESTED), or the request never reached the IdentityHub at all.
        logger.LogWarning(
            "The {Credential} credential request for application {ApplicationId} is still {State} after {MaxCredentialWaitTimeInDays} day(s); failing the step for retrigger. The IssuerService most likely accepted the request without delivering it. Requested type was {ConfiguredCredentialType}.",
            credential,
            context.ApplicationId,
            state,
            _settings.MaxCredentialWaitTimeInDays,
            configuredCredentialType);

        return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
            ProcessStepStatusId.FAILED,
            null,
            Enumerable.Repeat(retriggerStepTypeId, 1),
            null,
            false,
            $"No {credential} credential was issued within {_settings.MaxCredentialWaitTimeInDays} day(s)");
    }

    private async Task<string> GetBusinessPartnerNumber(Guid applicationId)
    {
        var (exists, _, businessPartnerNumber, _) = await portalRepositories.GetInstance<IApplicationRepository>()
            .GetBpnlCredentialIformationByApplicationId(applicationId).ConfigureAwait(ConfigureAwaitOptions.None);

        if (!exists)
        {
            throw new NotFoundException($"CompanyApplication {applicationId} does not exist");
        }

        return businessPartnerNumber ?? throw new ConflictException("The bpn must be set");
    }

    private async Task<DateTimeOffset> GetWaitingSince(Guid applicationId, ProcessStepTypeId awaitStepTypeId)
    {
        var processStepsDateCreated = await portalRepositories.GetInstance<IApplicationRepository>()
            .GetActiveProcessStepsDateCreated(applicationId, awaitStepTypeId).ConfigureAwait(ConfigureAwaitOptions.None);

        // The worker only invokes this because the step is active, so exactly one row is expected. Mirrors
        // the same guard on VALIDATE_DID_DOCUMENT rather than guessing a start time.
        var dateCreated = processStepsDateCreated.Take(2).ToList();
        return dateCreated.Count == 1
            ? dateCreated[0]
            : throw new ConflictException($"There must be exactly one active {awaitStepTypeId} for CompanyApplication {applicationId}");
    }
}
