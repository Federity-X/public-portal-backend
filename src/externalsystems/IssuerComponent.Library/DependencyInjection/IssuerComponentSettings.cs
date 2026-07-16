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

using Org.Eclipse.TractusX.Portal.Backend.Framework.Models.Configuration;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Token;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using System.ComponentModel.DataAnnotations;

namespace Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.DependencyInjection;

public class IssuerComponentSettings : KeyVaultAuthSettings
{
    [Required(AllowEmptyStrings = false)]
    public string BaseAddress { get; set; } = null!;

    [Required]
    public IEnumerable<EncryptionModeConfig> EncryptionConfigs { get; set; } = null!;

    [Required]
    public int EncryptionConfigIndex { get; set; }

    public string CallbackBaseUrl { get; set; } = null!;

    /// <summary>
    /// Which issuer a credential REQUEST step routes to. Null selects the DIM/ssi issuer-component
    /// (the historical default); <see cref="WalletProviderId.IdentityHub"/> selects the holder
    /// credential-request path. See BE-293-architecture-callback.
    /// </summary>
    public WalletProviderId? WalletProvider { get; set; }
}
