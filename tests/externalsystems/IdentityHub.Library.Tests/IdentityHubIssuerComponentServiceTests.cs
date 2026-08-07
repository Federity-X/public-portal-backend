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
using Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Models;
using Xunit;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.Tests;

public class IdentityHubIssuerComponentServiceTests
{
    private const string Bpn = "BPNL000000000001";
    private readonly IIdentityHubService _identityHubService;
    private readonly IdentityHubIssuerComponentService _sut;

    public IdentityHubIssuerComponentServiceTests()
    {
        _identityHubService = A.Fake<IIdentityHubService>();
        var options = Options.Create(new IdentityHubSettings
        {
            BaseAddress = "https://ih.example.org/api/identity",
            ApiKey = "key",
            DidDocumentBaseLocation = "ih.example.org",
            CredentialServiceBaseAddress = "https://ih.example.org/api/credentials",
            IssuerDid = "did:web:issuer.example.org:issuer",
            BpnCredentialType = "BpnCredential",
            BpnCredentialDefinitionId = "cd-bpn",
            MembershipCredentialType = "MembershipCredential",
            MembershipCredentialDefinitionId = "cd-membership"
        });
        _sut = new IdentityHubIssuerComponentService(_identityHubService, options);
    }

    [Fact]
    public async Task CreateBpnlCredential_TriggersBpnHolderCredentialRequest()
    {
        var result = await _sut.CreateBpnlCredential(new CreateBpnCredentialRequest("did:web:holder", Bpn, null, null), CancellationToken.None);

        result.Should().BeTrue();
        A.CallTo(() => _identityHubService.RequestCredentialAsync(Bpn, "BpnCredential", "cd-bpn", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task CreateMembershipCredential_TriggersMembershipHolderCredentialRequest()
    {
        var result = await _sut.CreateMembershipCredential(new CreateMembershipCredentialRequest("did:web:holder", Bpn, "catena-x", null, null), CancellationToken.None);

        result.Should().BeTrue();
        A.CallTo(() => _identityHubService.RequestCredentialAsync(Bpn, "MembershipCredential", "cd-membership", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task CreateFrameworkCredential_ThrowsConflict()
    {
        var request = new CreateFrameworkCredentialRequest("did:web:holder", Bpn, "framework", Guid.NewGuid(), null, null);

        Func<Task> act = () => _sut.CreateFrameworkCredential(request, "token", CancellationToken.None);

        // ConflictException (not NotSupportedException) so the error-handling middleware maps it to a
        // problem-detail response - this surfaces to an end user via companydata/useCaseParticipation.
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public void HolderRequestsOwnCredentials_IsTrue()
    {
        // The holder pulls its credentials over DCP, so the Portal must not gather or decrypt
        // technical-user credentials for it - its wallet row holds placeholder bytes.
        _sut.HolderRequestsOwnCredentials.Should().BeTrue();
    }
}
