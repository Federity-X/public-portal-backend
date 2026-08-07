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
/// Maps the onboarding wallet provider to the checklist step that provisions its wallet. Centralized so
/// the provider -&gt; create-wallet-step mapping lives in ONE place instead of being duplicated across the
/// business-logic classes that schedule the wallet step (BE-293).
/// </summary>
public static class WalletProviderIdExtensions
{
    /// <summary>
    /// Maps the wallet provider to the checklist step that provisions its wallet.
    /// </summary>
    public static ProcessStepTypeId GetCreateWalletStep(this WalletProviderId walletProvider) =>
        walletProvider switch
        {
            WalletProviderId.IdentityHub => ProcessStepTypeId.CREATE_IDENTITY_HUB_WALLET,
            WalletProviderId.Dim => ProcessStepTypeId.CREATE_DIM_WALLET,
            _ => ProcessStepTypeId.CREATE_IDENTITY_WALLET
        };
}
