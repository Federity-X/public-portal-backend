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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Models.Validation;
using Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.BusinessLogic;
using Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Service;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library;

public static class IdentityHubServiceCollectionExtension
{
    public static IServiceCollection AddIdentityHubService(this IServiceCollection services, IConfigurationSection section)
    {
        var optionsBuilder = services.AddOptions<IdentityHubSettings>().Bind(section);

        // Only enforce the (Required) IdentityHub settings when the section is actually configured,
        // so stub/DIM deployments that never select the IdentityHub wallet are not forced to supply
        // them. When IdentityHub IS the selected wallet the section is present, so validation fires
        // and fails fast on a misconfiguration.
        if (section.Exists() && section.GetChildren().Any())
        {
            optionsBuilder.EnvironmentalValidation(section);
        }

        // Base address read straight from config (no intermediate ServiceProvider). A placeholder is
        // used when unconfigured — the client is only ever invoked once IdentityHub is the selected
        // wallet, in which case the real address is validated above.
        var configuredBaseAddress = section["BaseAddress"];
        var baseAddress = string.IsNullOrWhiteSpace(configuredBaseAddress)
            ? "http://identity-hub.not-configured.invalid/"
            : configuredBaseAddress.EndsWith('/') ? configuredBaseAddress : $"{configuredBaseAddress}/";

        // Plain HTTP client — the IdentityHub admin API authenticates with an x-api-key
        // header (set per request), not the OAuth/KeyVault flow the DIM/Custodian clients use.
        services.AddHttpClient<IIdentityHubService, IdentityHubService>(client => client.BaseAddress = new Uri(baseAddress));
        services.AddTransient<IIdentityHubBusinessLogic, IdentityHubBusinessLogic>();

        // Keyed issuer-component impl selected when WalletProvider == IdentityHub: a credential
        // REQUEST triggers the holder credential-request path instead of the DIM/ssi HTTP call.
        services.AddKeyedTransient<IIssuerComponentService, IdentityHubIssuerComponentService>(WalletProviderId.IdentityHub);

        return services;
    }
}
