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
using Org.Eclipse.TractusX.Portal.Backend.BpnDidResolver.Library;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Models.Validation;
using Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.BusinessLogic;
using Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Service;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library;

public static class IdentityHubServiceCollectionExtension
{
    public static IServiceCollection AddIdentityHubService(this IServiceCollection services, IConfigurationSection section)
    {
        var options = services.AddOptions<IdentityHubSettings>().Bind(section);

        // The worker reacts to whatever wallet step is scheduled, so it cannot know at startup whether the
        // IdentityHub wallet is selected. We treat "the IdentityHub section is present" as the signal that
        // this deployment intends to use it: when present we enforce the (Required) settings (ValidateOnStart
        // fails fast on a partial/typo'd section) and wire the real admin-API client; when entirely absent
        // (a DIM/Custodian-only stack) we register guards instead of placeholder addresses, so a
        // CREATE_IDENTITY_HUB_WALLET step accidentally scheduled here (WalletProvider=IdentityHub without the
        // matching config) fails immediately with a clear, actionable error rather than an obscure DNS error.
        if (section.Exists() && section.GetChildren().Any())
        {
            options.EnvironmentalValidation(section);

            // Plain HTTP client — the IdentityHub admin API authenticates with an x-api-key
            // header (set per request), not the OAuth/KeyVault flow the DIM/Custodian clients use.
            services.AddHttpClient<IIdentityHubService, IdentityHubService>(client =>
                client.BaseAddress = new Uri(TrailingSlashed(section, "BaseAddress")));

            services.AddHttpClient(IdentityHubDidDocumentResolver.HttpClientName, client =>
                client.BaseAddress = new Uri(TrailingSlashed(section, "UniversalResolverAddress")));

            services.AddKeyedTransient<IDidDocumentResolver, IdentityHubDidDocumentResolver>(WalletProviderId.IdentityHub);
        }
        else
        {
            services.AddTransient<IIdentityHubService, NotConfiguredIdentityHubService>();
            services.AddKeyedTransient<IDidDocumentResolver, NotConfiguredIdentityHubService>(WalletProviderId.IdentityHub);
        }

        services.AddTransient<IIdentityHubBusinessLogic, IdentityHubBusinessLogic>();

        // Keyed issuer-component impl selected when WalletProvider == IdentityHub: a credential
        // REQUEST triggers the holder credential-request path instead of the DIM/ssi HTTP call.
        services.AddKeyedTransient<IIssuerComponentService, IdentityHubIssuerComponentService>(WalletProviderId.IdentityHub);

        return services;
    }

    private static string TrailingSlashed(IConfigurationSection section, string key)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ConfigurationException($"IdentityHub:{key} must be set when the IdentityHub wallet is configured");
        }

        return value.EndsWith('/') ? value : $"{value}/";
    }
}
