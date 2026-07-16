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

using System.ComponentModel.DataAnnotations;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library;

/// <summary>
/// Settings for driving the Tractus-X IdentityHub / IssuerService admin API when
/// IdentityHub is the selected onboarding wallet (BE-293).
/// </summary>
public class IdentityHubSettings
{
    /// <summary>Base address of the IdentityHub Identity API (admin), e.g. http://identity-hub.tx.test/api/identity.</summary>
    [Required(AllowEmptyStrings = false)]
    public string BaseAddress { get; set; } = null!;

    /// <summary>
    /// Super-user API key for the IdentityHub admin API (sent as the <c>x-api-key</c> header).
    /// Supplied via config/secret (not log-scraped) — see BE-293-2.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = null!;

    /// <summary>
    /// did:web base location the holder DID is built from, i.e. did:web:{DidDocumentBaseLocation}:{bpn}.
    /// Must resolve via the universal resolver and be registered in BDRS.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string DidDocumentBaseLocation { get; set; } = null!;

    /// <summary>
    /// Base address of the IdentityHub Credential Service API, used to build the holder's
    /// CredentialService serviceEndpoint baked into its ParticipantContext, i.e.
    /// {CredentialServiceBaseAddress}/v1/participants/{participantContextId}.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string CredentialServiceBaseAddress { get; set; } = null!;

    /// <summary>Role assigned to the holder ParticipantContext (the seed uses ROLE_USER).</summary>
    public string HolderRole { get; set; } = "ROLE_USER";

    /// <summary>
    /// DID of the IssuerService that issues the onboarding credentials, put into the holder's
    /// <c>credentials/request</c> body as <c>issuerDid</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string IssuerDid { get; set; } = null!;

    /// <summary>VC format requested from the issuer (the seed uses VC1_0_JWT).</summary>
    public string CredentialFormat { get; set; } = "VC1_0_JWT";

    /// <summary>VC type + issuer credential-definition id for the BPN(L) onboarding credential.</summary>
    [Required(AllowEmptyStrings = false)]
    public string BpnCredentialType { get; set; } = "BpnCredential";

    [Required(AllowEmptyStrings = false)]
    public string BpnCredentialDefinitionId { get; set; } = null!;

    /// <summary>VC type + issuer credential-definition id for the Membership onboarding credential.</summary>
    [Required(AllowEmptyStrings = false)]
    public string MembershipCredentialType { get; set; } = "MembershipCredential";

    [Required(AllowEmptyStrings = false)]
    public string MembershipCredentialDefinitionId { get; set; } = null!;
}
