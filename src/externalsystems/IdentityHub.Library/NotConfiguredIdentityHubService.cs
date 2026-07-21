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
using System.Text.Json;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library;

/// <summary>
/// Guard <see cref="IIdentityHubService"/> registered when the 'IdentityHub' configuration section is
/// absent (a DIM/Custodian-only deployment). It lets the checklist handler and issuer-component selector
/// be constructed, but fails loudly and clearly if a CREATE_IDENTITY_HUB_WALLET step is ever actually
/// executed here — i.e. WalletProvider=IdentityHub was selected without supplying the IdentityHub config.
/// </summary>
public class NotConfiguredIdentityHubService : IIdentityHubService
{
    private const string Message =
        "The IdentityHub wallet was invoked but the 'IdentityHub' configuration section is not configured for this deployment. " +
        "Set WalletProvider=IdentityHub only where the IdentityHub section (BaseAddress, ApiKey, IssuerService settings, …) is supplied.";

    public Task<(string Did, JsonDocument DidDocument)> CreateHolderWalletAsync(string bpn, string companyName, CancellationToken cancellationToken) =>
        throw new ConfigurationException(Message);

    public Task RequestCredentialAsync(string bpn, string credentialType, string credentialDefinitionId, CancellationToken cancellationToken) =>
        throw new ConfigurationException(Message);
}
