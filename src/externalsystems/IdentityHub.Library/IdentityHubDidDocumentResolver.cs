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

using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.BpnDidResolver.Library;
using Org.Eclipse.TractusX.Portal.Backend.Framework.HttpClientExtensions;
using System.Net.Http.Json;
using System.Text.Json;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library;

/// <summary>
/// <see cref="IDidDocumentResolver"/> for the IdentityHub wallet, resolving the holder's did:web
/// through the universal resolver configured under <c>IdentityHub</c>. This is what lets an
/// IdentityHub deployment run without a Dim configuration section at all.
/// </summary>
public class IdentityHubDidDocumentResolver(IHttpClientFactory httpClientFactory, IOptions<IdentityHubSettings> options)
    : IDidDocumentResolver
{
    /// <summary>Named client registered by AddIdentityHubService; kept distinct from DIM's "universalResolver".</summary>
    public const string HttpClientName = "identityHubUniversalResolver";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public int MaxValidationTimeInDays => options.Value.MaxValidationTimeInDays;

    /// <inheritdoc />
    public async Task<bool> ValidateDid(string did, CancellationToken cancellationToken)
    {
        using var httpClient = httpClientFactory.CreateClient(HttpClientName);
        // Same contract as the DIM resolver: a universal resolver answers 200 with a
        // didResolutionMetadata.error while the DID is not yet published, which is the "keep polling"
        // signal. Any non-2xx is a genuine infrastructure fault and surfaces as a ServiceException,
        // which the checklist error handler turns into a retriggerable step failure.
        var result = await httpClient.GetAsync($"1.0/identifiers/{Uri.EscapeDataString(did)}", cancellationToken)
            .CatchingIntoServiceExceptionFor("validate-did", HttpAsyncResponseMessageExtension.RecoverOptions.INFRASTRUCTURE).ConfigureAwait(false);

        var validationResult = await result.Content.ReadFromJsonAsync<DidResolutionResponse>(JsonOptions, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.None);
        return validationResult != null && string.IsNullOrWhiteSpace(validationResult.DidResolutionMetadata.Error);
    }
}

public record DidResolutionResponse(DidResolutionMetadata DidResolutionMetadata);

public record DidResolutionMetadata(string? Error);
