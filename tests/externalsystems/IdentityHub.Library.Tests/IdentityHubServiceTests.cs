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

using FluentAssertions;
using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.Tests;

public class IdentityHubServiceTests
{
    private const string Bpn = "BPNL000000000001";
    private const string ParticipantContextId = "bpnl000000000001"; // lowercased BPN (EDC 0.17.0 plain id)
    private const string Did = "did:web:ih.example.org:BPNL000000000001"; // original-case BPN in the did

    private static readonly IdentityHubSettings Settings = new()
    {
        BaseAddress = "https://ih.example.org/api/identity",
        ApiKey = "ih-key",
        DidDocumentBaseLocation = "ih.example.org",
        CredentialServiceBaseAddress = "https://ih.example.org/api/credentials",
        HolderRole = "ROLE_USER",
        IssuerDid = "did:web:issuer.example.org:issuer",
        CredentialFormat = "VC1_0_JWT",
        BpnCredentialType = "BpnCredential",
        BpnCredentialDefinitionId = "cd-bpn",
        MembershipCredentialType = "MembershipCredential",
        MembershipCredentialDefinitionId = "cd-membership",
        IssuerAdminBaseAddress = "https://issuer-admin.example.org/api/admin",
        IssuerAdminApiKey = "issuer-key",
        IssuerParticipantId = "issuer-bpnl00000003crhk",
        FrameworkContractVersion = "1.0"
    };

    private static IdentityHubService CreateSut(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://ih.example.org/api/identity/") }, Options.Create(Settings));

    [Fact]
    public async Task CreateHolderWalletAsync_PostsCreateThenActivate_WithCorrectCasing()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.OK, null));
        var sut = CreateSut(handler);

        var (did, didDocument) = await sut.CreateHolderWalletAsync(Bpn, "Test Corp", CancellationToken.None);

        did.Should().Be(Did);
        didDocument.RootElement.GetProperty("id").GetString().Should().Be(Did);

        handler.Requests.Should().HaveCount(2);

        var create = handler.Requests[0];
        create.Request.RequestUri!.AbsoluteUri.Should().Be("https://ih.example.org/api/identity/v1alpha/participants");
        create.Request.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("ih-key");
        using var body = JsonDocument.Parse(create.Body!);
        body.RootElement.GetProperty("participantContextId").GetString().Should().Be(ParticipantContextId);
        body.RootElement.GetProperty("did").GetString().Should().Be(Did);
        body.RootElement.GetProperty("serviceEndpoints")[0].GetProperty("serviceEndpoint").GetString()
            .Should().Be($"https://ih.example.org/api/credentials/v1/participants/{ParticipantContextId}");

        handler.Requests[1].Request.RequestUri!.AbsoluteUri.Should()
            .Be($"https://ih.example.org/api/identity/v1alpha/participants/{ParticipantContextId}/state?isActive=true");
    }

    [Fact]
    public async Task CreateHolderWalletAsync_CreateConflict_IsIdempotent()
    {
        // create -> 409 (already provisioned), activate -> 409 (already active): both tolerated.
        var handler = new RecordingHandler(_ => (HttpStatusCode.Conflict, null));
        var sut = CreateSut(handler);

        var (did, _) = await sut.CreateHolderWalletAsync(Bpn, "Test Corp", CancellationToken.None);

        did.Should().Be(Did);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateHolderWalletAsync_CreateFails_ThrowsServiceExceptionAndDoesNotActivate()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.InternalServerError, "boom"));
        var sut = CreateSut(handler);

        var ex = await Assert.ThrowsAsync<ServiceException>(() => sut.CreateHolderWalletAsync(Bpn, "Test Corp", CancellationToken.None));

        ex.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task RequestCredentialAsync_RegistersHolderThenRequestsCredential()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.OK, null));
        var sut = CreateSut(handler);

        await sut.RequestCredentialAsync(Bpn, "BpnCredential", "cd-bpn", CancellationToken.None);

        handler.Requests.Should().HaveCount(2);

        var register = handler.Requests[0];
        register.Request.RequestUri!.AbsoluteUri.Should()
            .Be("https://issuer-admin.example.org/api/admin/v1alpha/participants/issuer-bpnl00000003crhk/holders");
        register.Request.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("issuer-key");
        using var registerBody = JsonDocument.Parse(register.Body!);
        registerBody.RootElement.GetProperty("did").GetString().Should().Be(Did);
        registerBody.RootElement.GetProperty("holderId").GetString().Should().Be(Bpn);

        var request = handler.Requests[1];
        request.Request.RequestUri!.AbsoluteUri.Should()
            .Be($"https://ih.example.org/api/identity/v1alpha/participants/{ParticipantContextId}/credentials/request");
        request.Request.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be("ih-key");
        using var reqBody = JsonDocument.Parse(request.Body!);
        reqBody.RootElement.GetProperty("issuerDid").GetString().Should().Be("did:web:issuer.example.org:issuer");
        reqBody.RootElement.GetProperty("holderPid").GetString().Should().Be($"{ParticipantContextId}-bpncredential");
        var credential = reqBody.RootElement.GetProperty("credentials")[0];
        credential.GetProperty("id").GetString().Should().Be("cd-bpn");
        credential.GetProperty("type").GetString().Should().Be("BpnCredential");
        credential.GetProperty("format").GetString().Should().Be("VC1_0_JWT");
    }

    [Fact]
    public async Task RequestCredentialAsync_HolderAlreadyRegistered_IsIdempotent()
    {
        // register -> 409 (already registered), credential request -> 2xx
        var responses = new Queue<(HttpStatusCode, string?)>([(HttpStatusCode.Conflict, null), (HttpStatusCode.OK, null)]);
        var handler = new RecordingHandler(_ => responses.Dequeue());
        var sut = CreateSut(handler);

        await sut.RequestCredentialAsync(Bpn, "MembershipCredential", "cd-membership", CancellationToken.None);

        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task RequestCredentialAsync_HolderRegistrationFails_ThrowsAndDoesNotRequest()
    {
        var handler = new RecordingHandler(_ => (HttpStatusCode.InternalServerError, "nope"));
        var sut = CreateSut(handler);

        await Assert.ThrowsAsync<ServiceException>(() => sut.RequestCredentialAsync(Bpn, "BpnCredential", "cd-bpn", CancellationToken.None));

        handler.Requests.Should().ContainSingle(); // credential request not sent
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string? Body)> responder) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            var (status, respBody) = responder(request);
            var response = new HttpResponseMessage(status);
            if (respBody is not null)
            {
                response.Content = new StringContent(respBody);
            }

            return response;
        }
    }
}
