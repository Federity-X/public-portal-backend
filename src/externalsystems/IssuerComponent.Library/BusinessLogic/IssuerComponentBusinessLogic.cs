/********************************************************************************
 * Copyright (c) 2024 Contributors to the Eclipse Foundation
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

using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Models.Encryption;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Processes.Library.Enums;
using Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.DependencyInjection;
using Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Models;
using Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Service;
using Org.Eclipse.TractusX.Portal.Backend.Onboarding.WalletProvider;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Processes.ApplicationChecklist.Library;

namespace Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.BusinessLogic;

public class IssuerComponentBusinessLogic(
    IPortalRepositories repositories,
    IIssuerComponentServiceSelector serviceSelector,
    IApplicationChecklistService checklistService,
    IOptions<IssuerComponentSettings> options,
    IWalletProviderResolver walletProviderResolver)
    : IIssuerComponentBusinessLogic
{
    private readonly IssuerComponentSettings _settings = options.Value;

    private IIssuerComponentService Service => serviceSelector.GetForProvider(walletProviderResolver.Provider);

    public async Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> CreateBpnlCredential(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken)
    {
        var applicationId = context.ApplicationId;
        var (exists, holder, businessPartnerNumber, walletInformation) = await repositories.GetInstance<IApplicationRepository>().GetBpnlCredentialIformationByApplicationId(applicationId).ConfigureAwait(false);
        var isBringYourOwnWallet = await repositories.GetInstance<ICompanyRepository>().IsBringYourOwnWallet(applicationId).ConfigureAwait(false);
        if (!exists)
        {
            throw new NotFoundException($"CompanyApplication {applicationId} does not exist");
        }

        if (holder is null)
        {
            throw new ConflictException("The holder must be set");
        }

        if (businessPartnerNumber is null)
        {
            throw new ConflictException("The bpn must be set");
        }

        var service = Service;
        var technicalUserDetails = GetTechnicalUserDetails(walletInformation, isBringYourOwnWallet, service.HolderRequestsOwnCredentials);
        var callbackUrl = $"{_settings.CallbackBaseUrl}/api/administration/registration/issuer/bpncredential";
        var data = new CreateBpnCredentialRequest(holder, businessPartnerNumber, technicalUserDetails, callbackUrl);

        await service.CreateBpnlCredential(data, cancellationToken).ConfigureAwait(false);
        return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
            ProcessStepStatusId.DONE,
            checklist =>
            {
                checklist.ApplicationChecklistEntryStatusId = ApplicationChecklistEntryStatusId.IN_PROGRESS;
            },
            [ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE],
            null,
            true,
            null);
    }

    /// <summary>
    /// Builds the holder technical-user details the issuer acts with, or null when the issuer does not
    /// act on the holder's behalf. Gathering them is invalid when
    /// <see cref="IIssuerComponentService.HolderRequestsOwnCredentials"/>: such a holder stores
    /// placeholder bytes instead of an encrypted secret, and decrypting those would throw.
    /// </summary>
    private TechnicalUserDetails? GetTechnicalUserDetails(PortalBackend.DBAccess.Models.WalletInformation? walletInformation, bool isBringYourOwnWallet, bool holderRequestsOwnCredentials)
    {
        if (holderRequestsOwnCredentials)
        {
            return null;
        }

        if (walletInformation is null)
        {
            throw new ConflictException("The wallet information must be set");
        }

        if (isBringYourOwnWallet)
        {
            return null;
        }

        var cryptoConfig = _settings.EncryptionConfigs.SingleOrDefault(x => x.Index == walletInformation.EncryptionMode) ?? throw new ConfigurationException($"EncryptionModeIndex {walletInformation.EncryptionMode} is not configured");
        var secret = CryptoHelper.Decrypt(walletInformation.ClientSecret, walletInformation.InitializationVector, Convert.FromHexString(cryptoConfig.EncryptionKey), cryptoConfig.CipherMode, cryptoConfig.PaddingMode);
        return new TechnicalUserDetails(walletInformation.WalletUrl, walletInformation.ClientId, secret);
    }

    public async Task StoreBpnlCredentialResponse(Guid applicationId, IssuerResponseData data)
    {
        var context = await checklistService
            .VerifyChecklistEntryAndProcessSteps(
                applicationId,
                ApplicationChecklistEntryTypeId.BPNL_CREDENTIAL,
                [ApplicationChecklistEntryStatusId.IN_PROGRESS],
                ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE,
                processStepTypeIds: [ProcessStepTypeId.REQUEST_MEMBERSHIP_CREDENTIAL])
            .ConfigureAwait(false);

        checklistService.FinalizeChecklistEntryAndProcessSteps(
            context,
            item => item.ApplicationChecklistEntryStatusId = ApplicationChecklistEntryStatusId.IN_PROGRESS,
            item =>
            {
                item.ApplicationChecklistEntryStatusId = data.Status == IssuerResponseStatus.UNSUCCESSFUL
                    ? ApplicationChecklistEntryStatusId.FAILED
                    : ApplicationChecklistEntryStatusId.DONE;
                item.Comment = data.Message;
            },
            data.Status == IssuerResponseStatus.SUCCESSFUL
                ? [ProcessStepTypeId.REQUEST_MEMBERSHIP_CREDENTIAL]
                : null);
    }

    public async Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> CreateMembershipCredential(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken)
    {
        var applicationId = context.ApplicationId;
        var (exists, holder, businessPartnerNumber, walletInformation) = await repositories.GetInstance<IApplicationRepository>().GetBpnlCredentialIformationByApplicationId(applicationId).ConfigureAwait(false);
        var isBringYourOwnWallet = await repositories.GetInstance<ICompanyRepository>().IsBringYourOwnWallet(applicationId).ConfigureAwait(false);
        if (!exists)
        {
            throw new NotFoundException($"CompanyApplication {applicationId} does not exist");
        }

        if (holder is null)
        {
            throw new ConflictException("The holder must be set");
        }

        if (businessPartnerNumber is null)
        {
            throw new ConflictException("The bpn must be set");
        }

        var service = Service;
        var technicalUserDetails = GetTechnicalUserDetails(walletInformation, isBringYourOwnWallet, service.HolderRequestsOwnCredentials);
        var callbackUrl = $"{_settings.CallbackBaseUrl}/api/administration/registration/issuer/membershipcredential";

        var data = new CreateMembershipCredentialRequest(holder, businessPartnerNumber, "catena-x", technicalUserDetails, callbackUrl);

        await service.CreateMembershipCredential(data, cancellationToken).ConfigureAwait(false);
        return new IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult(
            ProcessStepStatusId.DONE,
            checklist =>
            {
                checklist.ApplicationChecklistEntryStatusId = ApplicationChecklistEntryStatusId.IN_PROGRESS;
            },
            [ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE],
            null,
            true,
            null);
    }

    public async Task StoreMembershipCredentialResponse(Guid applicationId, IssuerResponseData data)
    {
        var context = await checklistService
            .VerifyChecklistEntryAndProcessSteps(
                applicationId,
                ApplicationChecklistEntryTypeId.MEMBERSHIP_CREDENTIAL,
                [ApplicationChecklistEntryStatusId.IN_PROGRESS],
                ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE,
                processStepTypeIds: [ProcessStepTypeId.START_CLEARING_HOUSE])
            .ConfigureAwait(false);

        checklistService.FinalizeChecklistEntryAndProcessSteps(
            context,
            item => item.ApplicationChecklistEntryStatusId = ApplicationChecklistEntryStatusId.IN_PROGRESS,
            item =>
            {
                item.ApplicationChecklistEntryStatusId = data.Status == IssuerResponseStatus.UNSUCCESSFUL
                    ? ApplicationChecklistEntryStatusId.FAILED
                    : ApplicationChecklistEntryStatusId.DONE;
                item.Comment = data.Message;
            },
            data.Status == IssuerResponseStatus.SUCCESSFUL
                ? [ProcessStepTypeId.START_CLEARING_HOUSE]
                : null);
    }

    public async Task<Guid> CreateFrameworkCredentialData(Guid useCaseFrameworkVersionId, string frameworkId, Guid identityId, string token, CancellationToken cancellationToken)
    {
        var (holder, businessPartnerNumber, walletInformation) = await repositories.GetInstance<ICompanyRepository>().GetWalletData(identityId).ConfigureAwait(false);
        var isBringYourOwnWallet = await repositories.GetInstance<ICompanyRepository>().IsBringYourOwnWallet(identityId).ConfigureAwait(false);
        if (holder is null)
        {
            throw new ConflictException("The holder must be set");
        }

        if (businessPartnerNumber is null)
        {
            throw new ConflictException("The bpn must be set");
        }

        var service = Service;
        var technicalUserDetails = GetTechnicalUserDetails(walletInformation, isBringYourOwnWallet, service.HolderRequestsOwnCredentials);

        var data = new CreateFrameworkCredentialRequest(holder, businessPartnerNumber, frameworkId, useCaseFrameworkVersionId, technicalUserDetails, null);
        return await service.CreateFrameworkCredential(data, token, cancellationToken).ConfigureAwait(false);
    }
}
