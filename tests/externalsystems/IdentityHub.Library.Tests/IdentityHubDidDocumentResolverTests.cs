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

using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using System.Net;
using Xunit;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.Tests;

public class IdentityHubDidDocumentResolverTests
{
    private const string Did = "did:web:ih.example.org:BPNL000000000001";

    private static readonly IdentityHubSettings Settings = new()
    {
        BaseAddress = "https://ih.example.org/api/identity",
        ApiKey = "ih-key",
        DidDocumentBaseLocation = "ih.example.org",
        UniversalResolverAddress = "https://resolver.example.org",
        MaxValidationTimeInDays = 7,
        CredentialServiceBaseAddress = "https://ih.example.org/api/credentials",
        IssuerDid = "did:web:issuer.example.org:issuer",
        BpnCredentialDefinitionId = "cd-bpn",
        MembershipCredentialDefinitionId = "cd-membership",
        IssuerAdminBaseAddress = "https://issuer-admin.example.org/api/admin",
        IssuerAdminApiKey = "issuer-key",
        IssuerParticipantId = "issuer-bpnl00000003crhk"
    };

    private static (IdentityHubDidDocumentResolver Sut, StubHandler Handler) CreateSut(HttpStatusCode status, string? body)
    {
        var handler = new StubHandler(status, body);
        var factory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => factory.CreateClient(IdentityHubDidDocumentResolver.HttpClientName))
            .ReturnsLazily(() => new HttpClient(handler) { BaseAddress = new Uri("https://resolver.example.org/") });
        return (new IdentityHubDidDocumentResolver(factory, Options.Create(Settings)), handler);
    }

    [Fact]
    public void MaxValidationTimeInDays_ComesFromIdentityHubSettings()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, null);

        sut.MaxValidationTimeInDays.Should().Be(7);
    }

    [Fact]
    public async Task ValidateDid_WithResolvableDid_ReturnsTrueAndCallsTheIdentityHubResolver()
    {
        var (sut, handler) = CreateSut(HttpStatusCode.OK, """{"didResolutionMetadata":{}}""");

        var result = await sut.ValidateDid(Did, CancellationToken.None);

        result.Should().BeTrue();
        handler.LastRequestUri!.AbsoluteUri.Should()
            .Be($"https://resolver.example.org/1.0/identifiers/{Uri.EscapeDataString(Did)}");
    }

    [Fact]
    public async Task ValidateDid_WithResolutionError_ReturnsFalse()
    {
        var (sut, _) = CreateSut(HttpStatusCode.OK, """{"didResolutionMetadata":{"error":"notFound"}}""");

        var result = await sut.ValidateDid(Did, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateDid_WithNonSuccessStatus_ThrowsServiceException()
    {
        // Matches the DIM resolver: a broken resolver is an infrastructure fault, not a "not published
        // yet" signal, and the checklist error handler turns it into a retriggerable step failure.
        var (sut, _) = CreateSut(HttpStatusCode.ServiceUnavailable, null);

        Func<Task> act = () => sut.ValidateDid(Did, CancellationToken.None);

        await act.Should().ThrowAsync<ServiceException>();
    }

    private sealed class StubHandler(HttpStatusCode status, string? body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                response.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }
}
