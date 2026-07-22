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

using Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Models;

namespace Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Service;

public interface IIssuerComponentService
{
    /// <summary>
    /// Whether the holder requests its own credentials rather than this issuer acting on its behalf.
    /// <para>
    /// Default (<c>false</c>) is the DIM/ssi issuer-component contract: the Portal hands the issuer the
    /// holder's wallet technical-user credentials (wallet url, client id, decrypted client secret) and
    /// the issuer acts for the holder. <c>true</c> means the holder pulls the credential itself over DCP
    /// (IdentityHub) - its wallet row deliberately holds no usable secret, so the caller must not
    /// attempt to gather or decrypt one.
    /// </para>
    /// </summary>
    bool HolderRequestsOwnCredentials { get; }

    Task<bool> CreateBpnlCredential(CreateBpnCredentialRequest data, CancellationToken cancellationToken);
    Task<bool> CreateMembershipCredential(CreateMembershipCredentialRequest data, CancellationToken cancellationToken);
    Task<Guid> CreateFrameworkCredential(CreateFrameworkCredentialRequest data, string token, CancellationToken cancellationToken);
}
