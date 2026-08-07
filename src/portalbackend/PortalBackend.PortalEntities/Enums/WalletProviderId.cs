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
/// Selects which wallet/issuer the onboarding checklist provisions credentials into.
/// Replaces the legacy boolean <c>UseDimWallet</c> switch (kept for back-compat): a
/// null <c>WalletProvider</c> config value falls back to Dim when UseDimWallet=true,
/// else Custodian. SSI-DIM stays first-class as <see cref="Dim"/>.
/// </summary>
public enum WalletProviderId
{
    /// <summary>Legacy managed identity wallet (Custodian / MiW). Issues no BPN/Membership credentials.</summary>
    Custodian = 0,

    /// <summary>SAP DIM / decentralized identity wallet (the current default option).</summary>
    Dim = 1,

    /// <summary>Tractus-X IdentityHub as a Portal-managed holder wallet, issuing via the IdentityHub IssuerService (BE-293).</summary>
    IdentityHub = 2
}
