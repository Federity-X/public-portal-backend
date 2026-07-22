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
    IDateTimeProvider dateTimeProvider,
    IOptions<IdentityHubSettings> options,
    ILogger<IdentityHubCredentialAwaitBusinessLogic> logger)
    : IIdentityHubCredentialAwaitBusinessLogic
{
    private readonly IdentityHubSettings _settings = options.Value;

    /// <inheritdoc />
    public Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> AwaitBpnCredentialResponse(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken) =>
        AwaitCredentialResponse(context, ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL, "BPN", _settings.BpnCredentialType);

    /// <inheritdoc />
    public Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> AwaitMembershipCredentialResponse(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken) =>
        AwaitCredentialResponse(context, ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ProcessStepTypeId.RETRIGGER_REQUEST_MEMBERSHIP_CREDENTIAL, "Membership", _settings.MembershipCredentialType);

    private async Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> AwaitCredentialResponse(
        IApplicationChecklistService.WorkerChecklistProcessStepData context,
        ProcessStepTypeId awaitStepTypeId,
        ProcessStepTypeId retriggerStepTypeId,
        string credential,
        string configuredCredentialType)
    {
        var dateCreated = await GetWaitingSince(context.ApplicationId, awaitStepTypeId).ConfigureAwait(ConfigureAwaitOptions.None);
        var deadline = dateCreated.AddDays(_settings.MaxCredentialWaitTimeInDays);
        if (dateTimeProvider.OffsetNow <= deadline)
        {
            // Still within budget. Stay in TODO without touching the checklist entry, so the callback can
            // still finalize this step exactly as it does today - this handler only ever adds a deadline,
            // it never competes with the callback for the happy path.
            return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
                ProcessStepStatusId.TODO,
                null,
                null,
                null,
                false,
                null);
        }

        // Ordered by likelihood given what a missing callback can actually mean. A holder-side send failure
        // is NOT in this list: that transitions the holder to ERROR immediately and posts UNSUCCESSFUL, so
        // it fails in seconds rather than reaching this deadline.
        logger.LogWarning(
            "No {Credential} credential callback for application {ApplicationId} within {MaxCredentialWaitTimeInDays} day(s); failing the step for retrigger. Likely causes: the IssuerService accepted the request but never delivered it (the holder stays in REQUESTED and reports nothing); the portal-credential-callback extension is not deployed or cannot reach the Portal; or its configured credential type does not match the {ConfiguredCredentialType} this Portal requested.",
            credential,
            context.ApplicationId,
            _settings.MaxCredentialWaitTimeInDays,
            configuredCredentialType);

        return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
            ProcessStepStatusId.FAILED,
            null,
            Enumerable.Repeat(retriggerStepTypeId, 1),
            null,
            false,
            $"No {credential} credential response was received within {_settings.MaxCredentialWaitTimeInDays} day(s)");
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
