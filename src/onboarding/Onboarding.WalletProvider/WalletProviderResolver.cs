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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;

namespace Org.Eclipse.TractusX.Portal.Backend.Onboarding.WalletProvider;

/// <inheritdoc />
public class WalletProviderResolver : IWalletProviderResolver
{
    public WalletProviderResolver(IOptions<OnboardingWalletSettings> options, ILogger<WalletProviderResolver> logger)
    {
        Provider = options.Value.WalletProvider;
        // Singleton — logged once, effectively at startup, so the resolved provider is visible in the logs
        // of every deployable that participates in onboarding.
        logger.LogInformation("Onboarding wallet provider resolved: {WalletProvider}", Provider);
    }

    /// <inheritdoc />
    public WalletProviderId Provider { get; }
}
