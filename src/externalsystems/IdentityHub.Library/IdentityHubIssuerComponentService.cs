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

using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Models;
using Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Service;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library;

/// <summary>
/// <see cref="IIssuerComponentService"/> for the IdentityHub wallet: a credential REQUEST triggers
/// the holder to request the credential from the IssuerService via DCP
/// (<c>credentials/request</c>). It does NOT wait for completion — the holder-side portal-callback
/// extension observes the request reaching ISSUED and posts the Portal's existing BPN-keyed issuer
/// callback, which advances the AWAIT_*_CREDENTIAL_RESPONSE step. See BE-293-architecture-callback.
/// </summary>
public class IdentityHubIssuerComponentService(IIdentityHubService identityHubService, IOptions<IdentityHubSettings> options)
    : IIssuerComponentService
{
    private readonly IdentityHubSettings _settings = options.Value;

    /// <inheritdoc />
    /// <remarks>
    /// The holder requests its own credentials from the IssuerService over DCP, so the Portal never acts
    /// on its behalf. Its wallet row stores placeholder bytes rather than a real client secret, and
    /// attempting to decrypt one would fail.
    /// </remarks>
    public bool HolderRequestsOwnCredentials => true;

    public async Task<bool> CreateBpnlCredential(CreateBpnCredentialRequest data, CancellationToken cancellationToken)
    {
        await identityHubService.RequestCredentialAsync(data.BusinessPartnerNumber, _settings.BpnCredentialType, _settings.BpnCredentialDefinitionId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> CreateMembershipCredential(CreateMembershipCredentialRequest data, CancellationToken cancellationToken)
    {
        await identityHubService.RequestCredentialAsync(data.HolderBpn, _settings.MembershipCredentialType, _settings.MembershipCredentialDefinitionId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task<Guid> CreateFrameworkCredential(CreateFrameworkCredentialRequest data, string token, CancellationToken cancellationToken) =>
        // Use-case framework credentials are a post-onboarding flow and are out of scope for the
        // BE-293 IdentityHub onboarding wallet. Fail loud rather than silently no-op (repo rule:
        // avoid fallbacks) so a mis-routed framework request is caught, not lost. ConflictException
        // (not NotSupportedException) so the middleware maps it to a problem-detail response instead
        // of an unmapped 500 - this reaches an end user through POST companydata/useCaseParticipation.
        throw new ConflictException("Framework credential issuance via IdentityHub is not supported (the IdentityHub onboarding wallet covers BPN + Membership credentials only).");
}
