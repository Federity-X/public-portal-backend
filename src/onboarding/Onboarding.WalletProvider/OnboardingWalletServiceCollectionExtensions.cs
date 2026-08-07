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

namespace Org.Eclipse.TractusX.Portal.Backend.Onboarding.WalletProvider;

public static class OnboardingWalletServiceCollectionExtensions
{
    /// <summary>Top-level configuration section holding the single onboarding wallet-provider selection.</summary>
    public const string ConfigSection = "Onboarding";

    /// <summary>
    /// Binds the single <see cref="OnboardingWalletSettings"/> from the top-level <c>Onboarding</c> section
    /// and registers <see cref="IWalletProviderResolver"/>. Call once per deployable (Administration,
    /// Registration, Processes.Worker) — mirrors <c>AddPortalRepositories(IConfiguration)</c>, which every
    /// deployable also calls with the whole configuration to resolve a fixed key.
    /// </summary>
    public static IServiceCollection AddOnboardingWallet(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigSection);
        services.AddOptions<OnboardingWalletSettings>()
            .Bind(section)
            .EnvironmentalValidation(section);
        services.AddSingleton<IWalletProviderResolver, WalletProviderResolver>();
        return services;
    }
}
