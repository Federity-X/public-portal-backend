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

namespace Org.Eclipse.TractusX.Portal.Backend.BpnDidResolver.Library;

/// <summary>
/// Resolves a holder's did:web through a universal resolver, so the VALIDATE_DID_DOCUMENT checklist
/// step can wait until the wallet provider has actually published the DID document.
/// <para>
/// Implementations are registered as keyed services (keyed on <c>WalletProviderId</c>), so each wallet
/// provider brings its own resolver endpoint and its own patience budget without any provider having
/// to configure another provider's settings.
/// </para>
/// </summary>
public interface IDidDocumentResolver
{
    /// <summary>How long the DID may stay unresolvable before the step is failed for manual retrigger.</summary>
    int MaxValidationTimeInDays { get; }

    /// <summary>True once the did resolves cleanly through the universal resolver.</summary>
    Task<bool> ValidateDid(string did, CancellationToken cancellationToken);
}
