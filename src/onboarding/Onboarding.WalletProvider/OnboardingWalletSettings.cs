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

using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using System.ComponentModel.DataAnnotations;

namespace Org.Eclipse.TractusX.Portal.Backend.Onboarding.WalletProvider;

/// <summary>
/// Deployment-wide selection of the onboarding wallet/issuer provider (BE-293). This is the single
/// source of truth read by every process (Administration, Registration, Processes.Worker) through
/// <see cref="IWalletProviderResolver"/>; it replaces the former per-feature WalletProvider/UseDimWallet
/// settings that were duplicated across five settings classes.
/// </summary>
public class OnboardingWalletSettings
{
    /// <summary>
    /// Which wallet/issuer the onboarding checklist provisions credentials into
    /// (<see cref="WalletProviderId.Dim"/>, <see cref="WalletProviderId.IdentityHub"/> or
    /// <see cref="WalletProviderId.Custodian"/>).
    /// <para>
    /// Deliberately required with no default. This setting replaces the per-feature UseDimWallet flags,
    /// whose shipped value was false (i.e. Custodian); defaulting it either way would silently move some
    /// existing deployment onto a different wallet on upgrade. Failing to start with a message naming
    /// the key is the safer trade, and it is a one-line change per environment.
    /// </para>
    /// </summary>
    [Required]
    public WalletProviderId? WalletProvider { get; set; }
}
