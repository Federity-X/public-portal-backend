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

using Org.Eclipse.TractusX.Portal.Backend.Framework.DateTimeProvider;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Processes.Library.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Onboarding.WalletProvider;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Processes.ApplicationChecklist.Library;

namespace Org.Eclipse.TractusX.Portal.Backend.BpnDidResolver.Library.BusinessLogic;

/// <summary>
/// Drives the VALIDATE_DID_DOCUMENT checklist step against whichever universal resolver the
/// deployment's wallet provider supplies. Previously this lived in the DIM business logic, which
/// forced an IdentityHub-only deployment to configure a full (and fully validated) Dim section just
/// to reach the resolver. See <see cref="IDidDocumentResolver"/>.
/// </summary>
public class DidDocumentValidationBusinessLogic(
    IPortalRepositories portalRepositories,
    IDidDocumentResolverSelector resolverSelector,
    IWalletProviderResolver walletProviderResolver,
    IDateTimeProvider dateTimeProvider)
    : IDidDocumentValidationBusinessLogic
{
    /// <inheritdoc />
    public async Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> ValidateDidDocument(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken)
    {
        if (context.Checklist[ApplicationChecklistEntryTypeId.IDENTITY_WALLET] != ApplicationChecklistEntryStatusId.IN_PROGRESS)
        {
            return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
                ProcessStepStatusId.FAILED,
                checklistEntry => checklistEntry.Comment = $"processStep CREATE_IDENTITY_WALLET failed as entries IDENTITY_WALLET must have status {ApplicationChecklistEntryStatusId.IN_PROGRESS}",
                null,
                null,
                true,
                null);
        }

        var resolver = resolverSelector.GetForProvider(walletProviderResolver.Provider);
        var (result, dateCreated) = await ValidateDid(resolver, context.ApplicationId, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);
        if (result)
        {
            return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
                ProcessStepStatusId.DONE,
                checklist =>
                {
                    checklist.ApplicationChecklistEntryStatusId = ApplicationChecklistEntryStatusId.IN_PROGRESS;
                },
                [ProcessStepTypeId.TRANSMIT_BPN_DID],
                null,
                true,
                null);
        }

        // Not resolvable yet: stay in TODO so the worker re-polls, until the provider's patience budget
        // runs out - then fail for a manual RETRIGGER_VALIDATE_DID_DOCUMENT.
        var maxTime = dateCreated.AddDays(resolver.MaxValidationTimeInDays);
        return dateTimeProvider.OffsetNow > maxTime
            ? new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
                ProcessStepStatusId.FAILED,
                null,
                Enumerable.Repeat(ProcessStepTypeId.RETRIGGER_VALIDATE_DID_DOCUMENT, 1),
                null,
                false,
                "The validation was aborted")
            : new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
                ProcessStepStatusId.TODO,
                null,
                null,
                null,
                false,
                null);
    }

    private async Task<(bool ValidationResult, DateTimeOffset DateCreated)> ValidateDid(IDidDocumentResolver resolver, Guid applicationId, CancellationToken cancellationToken)
    {
        var (exists, did, processStepsDateCreated) = await portalRepositories.GetInstance<IApplicationRepository>().GetDidApplicationId(applicationId).ConfigureAwait(ConfigureAwaitOptions.None);
        if (!exists)
        {
            throw new NotFoundException($"CompanyApplication {applicationId} does not exist");
        }

        if (string.IsNullOrWhiteSpace(did))
        {
            throw new ConflictException("There must be a did set");
        }

        if (processStepsDateCreated.Count() != 1)
        {
            throw new ConflictException($"There must be excatly on active {ProcessStepTypeId.VALIDATE_DID_DOCUMENT}");
        }

        return (await resolver.ValidateDid(did, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None), processStepsDateCreated.Single());
    }
}
