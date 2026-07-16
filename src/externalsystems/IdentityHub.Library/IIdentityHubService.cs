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

using System.Text.Json;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library;

/// <summary>
/// Thin client for the IdentityHub Identity/IssuerService admin API. Provisions a
/// holder ParticipantContext (the company's managed wallet) and returns its did:web
/// + DID document, mirroring the flow the umbrella post-install-identityhub-seed hook
/// already drives end-to-end.
/// </summary>
public interface IIdentityHubService
{
    /// <summary>
    /// Creates (idempotently) a holder ParticipantContext for the given BPN on the
    /// IdentityHub and returns its did:web and resolved DID document.
    /// </summary>
    /// <param name="bpn">The company's business partner number (already assigned by BPDM).</param>
    /// <param name="companyName">The company display name.</param>
    /// <param name="cancellationToken">CancellationToken.</param>
    /// <returns>The holder's did:web and its DID document.</returns>
    Task<(string Did, JsonDocument DidDocument)> CreateHolderWalletAsync(string bpn, string companyName, CancellationToken cancellationToken);

    /// <summary>
    /// Triggers the holder to request a credential from the IssuerService via DCP
    /// (<c>POST /v1alpha/participants/{ctx}/credentials/request</c>). This only initiates the
    /// request; completion is observed out-of-band by the IdentityHub-side portal-callback
    /// extension (which posts the Portal's BPN-keyed issuer callback when the holder request
    /// reaches ISSUED). Idempotent per (holder, credential type) via a deterministic holderPid.
    /// </summary>
    /// <param name="bpn">The holder's business partner number (its participant context id is derived from it).</param>
    /// <param name="credentialType">The VC type to request (e.g. BpnCredential, MembershipCredential).</param>
    /// <param name="credentialDefinitionId">The issuer credential-definition id for that type.</param>
    /// <param name="cancellationToken">CancellationToken.</param>
    Task RequestCredentialAsync(string bpn, string credentialType, string credentialDefinitionId, CancellationToken cancellationToken);
}
