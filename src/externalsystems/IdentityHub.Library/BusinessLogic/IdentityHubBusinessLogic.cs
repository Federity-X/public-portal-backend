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

using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Processes.Library.Enums;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Processes.ApplicationChecklist.Library;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.BusinessLogic;

public class IdentityHubBusinessLogic(IPortalRepositories portalRepositories, IIdentityHubService identityHubService)
    : IIdentityHubBusinessLogic
{
    /// <inheritdoc />
    public async Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> CreateIdentityHubWalletAsync(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken)
    {
        if (context.Checklist[ApplicationChecklistEntryTypeId.BUSINESS_PARTNER_NUMBER] == ApplicationChecklistEntryStatusId.FAILED || context.Checklist[ApplicationChecklistEntryTypeId.REGISTRATION_VERIFICATION] == ApplicationChecklistEntryStatusId.FAILED)
        {
            return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
                ProcessStepStatusId.SKIPPED,
                checklistEntry => checklistEntry.Comment = $"processStep CREATE_IDENTITY_HUB_WALLET skipped as entries BUSINESS_PARTNER_NUMBER and REGISTRATION_VERIFICATION have status {context.Checklist[ApplicationChecklistEntryTypeId.BUSINESS_PARTNER_NUMBER]} and {context.Checklist[ApplicationChecklistEntryTypeId.REGISTRATION_VERIFICATION]}",
                null,
                null,
                true,
                null);
        }

        if (context.Checklist[ApplicationChecklistEntryTypeId.BUSINESS_PARTNER_NUMBER] == ApplicationChecklistEntryStatusId.DONE && context.Checklist[ApplicationChecklistEntryTypeId.REGISTRATION_VERIFICATION] == ApplicationChecklistEntryStatusId.DONE)
        {
            var did = await CreateWalletInternal(context.ApplicationId, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);

            return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
                ProcessStepStatusId.DONE,
                checklist =>
                {
                    checklist.ApplicationChecklistEntryStatusId = ApplicationChecklistEntryStatusId.IN_PROGRESS;
                    checklist.Comment = $"IdentityHub holder wallet created: {did}";
                },
                // Hand off to the existing DID-validation chain (VALIDATE_DID_DOCUMENT ->
                // TRANSMIT_BPN_DID -> REQUEST_BPN_CREDENTIAL), shared with the DIM path.
                new[] { ProcessStepTypeId.VALIDATE_DID_DOCUMENT },
                null,
                true,
                null);
        }

        return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(ProcessStepStatusId.TODO, null, null, null, false, null);
    }

    private async Task<string> CreateWalletInternal(Guid applicationId, CancellationToken cancellationToken)
    {
        var (companyId, companyName, bpn) = await portalRepositories.GetInstance<IApplicationRepository>()
            .GetCompanyAndApplicationDetailsForCreateWalletAsync(applicationId).ConfigureAwait(ConfigureAwaitOptions.None);

        if (string.IsNullOrWhiteSpace(bpn))
        {
            throw new ConflictException($"BusinessPartnerNumber is not set for application {applicationId}");
        }

        var (did, didDocument) = await identityHubService.CreateHolderWalletAsync(bpn, companyName, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);

        // Persist the holder did:web on the company (same store the BYOW did:web path uses).
        await portalRepositories.GetInstance<ICompanyRepository>()
            .CreateCustomerWallet(companyId, did, didDocument).ConfigureAwait(ConfigureAwaitOptions.None);

        // Also set the company's DidDocumentLocation (as the DIM/BYOW paths do): the downstream
        // REQUEST_{BPN,MEMBERSHIP}_CREDENTIAL step guards on it ("The holder must be set") even though
        // the IdentityHub issuer path itself requests by BPN. Without this the credential requests fail.
        portalRepositories.GetInstance<ICompanyRepository>()
            .AttachAndModifyCompany(companyId, c => c.DidDocumentLocation = null, c => c.DidDocumentLocation = did);

        return did;
    }
}
