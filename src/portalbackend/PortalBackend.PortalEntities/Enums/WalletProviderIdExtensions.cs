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

namespace Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;

/// <summary>
/// Shared resolution of the onboarding wallet provider. Centralized so the null-fallback rule
/// (a null <c>WalletProvider</c> config value falls back to the legacy <c>UseDimWallet</c> bool) and
/// the wallet-provider -&gt; create-wallet step mapping live in ONE place — the settings classes that
/// expose <c>WalletProvider</c>/<c>UseDimWallet</c> are independent, so duplicating either rule risks
/// them silently diverging (BE-293).
/// </summary>
public static class WalletProviderIdExtensions
{
    /// <summary>
    /// Resolves the effective <see cref="WalletProviderId"/> from the (nullable) configured value,
    /// falling back to the legacy <paramref name="useDimWallet"/> bool when unset
    /// (true =&gt; <see cref="WalletProviderId.Dim"/>, false =&gt; <see cref="WalletProviderId.Custodian"/>).
    /// </summary>
    public static WalletProviderId EffectiveWalletProvider(this WalletProviderId? walletProvider, bool useDimWallet) =>
        walletProvider ?? (useDimWallet ? WalletProviderId.Dim : WalletProviderId.Custodian);

    /// <summary>
    /// Maps the effective wallet provider to the checklist step that provisions its wallet.
    /// </summary>
    public static ProcessStepTypeId GetCreateWalletStep(this WalletProviderId walletProvider) =>
        walletProvider switch
        {
            WalletProviderId.IdentityHub => ProcessStepTypeId.CREATE_IDENTITY_HUB_WALLET,
            WalletProviderId.Dim => ProcessStepTypeId.CREATE_DIM_WALLET,
            _ => ProcessStepTypeId.CREATE_IDENTITY_WALLET
        };
}
