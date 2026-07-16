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
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library;

/// <summary>
/// Drives the IdentityHub Identity API to provision a holder ParticipantContext, mirroring
/// the proven umbrella post-install-identityhub-seed flow (step 2 create + step 3 activate):
/// <list type="bullet">
/// <item>POST /v1alpha/participants — create the holder context with an inline-generated
///   keypair and its CredentialService serviceEndpoint (the DCP walkthrough step 02 body).</item>
/// <item>POST /v1alpha/participants/{ctx}/state?isActive=true — activate it.</item>
/// </list>
/// </summary>
public class IdentityHubService(HttpClient httpClient, IOptions<IdentityHubSettings> options)
    : IIdentityHubService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IdentityHubSettings _settings = options.Value;

    /// <inheritdoc />
    public async Task<(string Did, JsonDocument DidDocument)> CreateHolderWalletAsync(string bpn, string companyName, CancellationToken cancellationToken)
    {
        // EDC/IdentityHub 0.17.0 uses a PLAIN participantContextId in URL paths and in the
        // CredentialService serviceEndpoint baked into the DID document.
        var participantContextId = bpn.ToLowerInvariant();
        var did = $"did:web:{_settings.DidDocumentBaseLocation}:{bpn}";
        var credentialServiceUrl = $"{_settings.CredentialServiceBaseAddress.TrimEnd('/')}/v1/participants/{participantContextId}";

        var body = new CreateParticipantRequest(
            true,
            participantContextId,
            did,
            [_settings.HolderRole],
            new ParticipantKey(
                $"{did}#key-1",
                "JsonWebKey2020",
                $"{participantContextId}-key-1",
                new KeyGeneratorParams("EC", "secp256r1")),
            [new ServiceEndpoint($"{did}#CredentialService", "CredentialService", credentialServiceUrl)]);

        var created = await PostWithApiKeyAsync("v1alpha/participants", body, $"create holder participant-context for BPN {bpn}", cancellationToken).ConfigureAwait(false);
        // On 201 the IdentityHub returns {apiKey, clientId, clientSecret}; clientSecret (the
        // holder's STS secret) is returned ONLY on this first create and is not recoverable on
        // 409. It is required later by the participant's connector (edc-wallet-secret) for DCP
        // data exchange — propagating it to the connector Vault is tracked separately (BE-293);
        // it is NOT needed for onboarding credential issuance, which is the scope here.
        if (created is not null)
        {
            _ = created.ClientSecret;
        }

        // Activate the context (idempotent — 409/2xx both fine).
        await PostWithApiKeyAsync($"v1alpha/participants/{participantContextId}/state?isActive=true", null, $"activate holder participant-context for BPN {bpn}", cancellationToken).ConfigureAwait(false);

        // The DID document is fully resolved + validated downstream by the VALIDATE_DID_DOCUMENT
        // checklist step (universal resolver + DID schema); persist the did:web reference here.
        var didDocument = JsonSerializer.SerializeToDocument(new DidReference(did));
        return (did, didDocument);
    }

    /// <inheritdoc />
    public async Task RequestCredentialAsync(string bpn, string credentialType, string credentialDefinitionId, CancellationToken cancellationToken)
    {
        var participantContextId = bpn.ToLowerInvariant();
        // holderPid is the PRIMARY KEY of the holder-credential-request store, so it MUST be
        // unique per (participant, credential type) — a constant collides across participants on
        // the persistent store (only the first request inserts). Deterministic so the request is
        // idempotent across step retries for the same holder+type.
        var holderPid = $"{participantContextId}-{credentialType.ToLowerInvariant()}";
        var body = new CredentialRequest(
            _settings.IssuerDid,
            holderPid,
            [new RequestedCredential(credentialDefinitionId, credentialType, _settings.CredentialFormat)]);

        await PostWithApiKeyAsync(
            $"v1alpha/participants/{participantContextId}/credentials/request",
            body,
            $"request {credentialType} for BPN {bpn}",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CreateParticipantResponse?> PostWithApiKeyAsync(string relativeUrl, object? body, string action, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl);
        request.Headers.Add("x-api-key", _settings.ApiKey);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // Already provisioned — idempotent success (the create response/secret is not returned).
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new ServiceException($"IdentityHub {action} failed with status {response.StatusCode}: {content}", response.StatusCode);
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<CreateParticipantResponse>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Activation and some responses carry no JSON body.
            return null;
        }
    }
}

public record CreateParticipantRequest(
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("participantContextId")] string ParticipantContextId,
    [property: JsonPropertyName("did")] string Did,
    [property: JsonPropertyName("roles")] IEnumerable<string> Roles,
    [property: JsonPropertyName("key")] ParticipantKey Key,
    [property: JsonPropertyName("serviceEndpoints")] IEnumerable<ServiceEndpoint> ServiceEndpoints);

public record ParticipantKey(
    [property: JsonPropertyName("keyId")] string KeyId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("privateKeyAlias")] string PrivateKeyAlias,
    [property: JsonPropertyName("keyGeneratorParams")] KeyGeneratorParams KeyGeneratorParams);

public record KeyGeneratorParams(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("curve")] string Curve);

public record ServiceEndpoint(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("serviceEndpoint")] string ServiceEndpointUrl);

public record CreateParticipantResponse(
    [property: JsonPropertyName("apiKey")] string? ApiKey,
    [property: JsonPropertyName("clientId")] string? ClientId,
    [property: JsonPropertyName("clientSecret")] string? ClientSecret);

public record DidReference(
    [property: JsonPropertyName("id")] string Id);

public record CredentialRequest(
    [property: JsonPropertyName("issuerDid")] string IssuerDid,
    [property: JsonPropertyName("holderPid")] string HolderPid,
    [property: JsonPropertyName("credentials")] IEnumerable<RequestedCredential> Credentials);

public record RequestedCredential(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("format")] string Format);
