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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.BpnDidResolver.Library;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.BusinessLogic;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Xunit;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.Tests;

public class IdentityHubServiceCollectionExtensionTests
{
    private static readonly Dictionary<string, string?> Complete = new()
    {
        ["IdentityHub:BaseAddress"] = "https://ih-admin.example.org/api/identity",
        ["IdentityHub:ApiKey"] = "ih-key",
        ["IdentityHub:DidDocumentBaseLocation"] = "ih.example.org",
        ["IdentityHub:UniversalResolverAddress"] = "https://resolver.example.org",
        ["IdentityHub:MaxValidationTimeInDays"] = "7",
        ["IdentityHub:CredentialServiceBaseAddress"] = "https://ih.example.org/api/credentials",
        ["IdentityHub:IssuerDid"] = "did:web:issuer.example.org:issuer",
        ["IdentityHub:BpnCredentialDefinitionId"] = "cd-bpn",
        ["IdentityHub:MembershipCredentialDefinitionId"] = "cd-membership",
        ["IdentityHub:IssuerAdminBaseAddress"] = "https://issuer-admin.example.org/api/admin",
        ["IdentityHub:IssuerAdminApiKey"] = "issuer-key",
        ["IdentityHub:IssuerParticipantId"] = "issuer-bpnl00000003crhk",
    };

    private static IServiceProvider Build(Action<Dictionary<string, string?>>? mutate = null)
    {
        var values = new Dictionary<string, string?>(Complete);
        mutate?.Invoke(values);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddIdentityHubService(configuration.GetSection("IdentityHub"))
            .BuildServiceProvider();
    }

    [Fact]
    public void WithCompleteSection_WiresTheRealClientAndResolver()
    {
        var sut = Build();

        sut.GetRequiredService<IIdentityHubService>().Should().BeOfType<IdentityHubService>();
        sut.GetRequiredKeyedService<IDidDocumentResolver>(WalletProviderId.IdentityHub)
            .Should().BeOfType<IdentityHubDidDocumentResolver>();
        sut.GetRequiredService<IOptions<IdentityHubSettings>>().Value.MaxValidationTimeInDays.Should().Be(7);
    }

    [Fact]
    public void CredentialAwaitLogic_IsRegisteredForEveryDeployment()
    {
        // ApplicationChecklistHandlerService takes it unconditionally and decides per provider whether to
        // use it, so a missing registration would break worker startup for DIM and Custodian too.
        var configuration = new ConfigurationBuilder().Build();
        var descriptors = new ServiceCollection().AddIdentityHubService(configuration.GetSection("IdentityHub"));

        descriptors.Should().ContainSingle(d => d.ServiceType == typeof(IIdentityHubCredentialAwaitBusinessLogic));
    }

    [Fact]
    public void WithoutSection_RegistersTheGuardForBothExtensionPoints()
    {
        var configuration = new ConfigurationBuilder().Build();
        var sut = new ServiceCollection()
            .AddIdentityHubService(configuration.GetSection("IdentityHub"))
            .BuildServiceProvider();

        sut.GetRequiredService<IIdentityHubService>().Should().BeOfType<NotConfiguredIdentityHubService>();
        sut.GetRequiredKeyedService<IDidDocumentResolver>(WalletProviderId.IdentityHub)
            .Should().BeOfType<NotConfiguredIdentityHubService>();
    }

    [Theory]
    [InlineData("IdentityHub:UniversalResolverAddress")]
    [InlineData("IdentityHub:BaseAddress")]
    [InlineData("IdentityHub:ApiKey")]
    [InlineData("IdentityHub:IssuerParticipantId")]
    public void WithAMissingRequiredKey_FailsValidation(string key)
    {
        var sut = Build(v => v.Remove(key));

        var act = () => sut.GetRequiredService<IOptions<IdentityHubSettings>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData(null)]   // key absent entirely - binds to 0
    [InlineData("0")]
    [InlineData("-1")]
    public void WithAnUnusableMaxValidationTime_FailsValidation(string? configured)
    {
        // [Required] would pass here: a non-nullable int is never null, so 0 validates. That would
        // start cleanly and then fail VALIDATE_DID_DOCUMENT on its first poll, because the deadline
        // would be dateCreated + 0 days. The deployment must be told at startup instead.
        var sut = Build(v =>
        {
            if (configured is null)
            {
                v.Remove("IdentityHub:MaxValidationTimeInDays");
            }
            else
            {
                v["IdentityHub:MaxValidationTimeInDays"] = configured;
            }
        });

        var act = () => sut.GetRequiredService<IOptions<IdentityHubSettings>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain(nameof(IdentityHubSettings.MaxValidationTimeInDays));
    }

    [Fact]
    public void WithAPartialSection_FailsRatherThanStartingHalfConfigured()
    {
        // A section that exists but is incomplete is the realistic misconfiguration - a helm overlay
        // that forgot a key. It must not fall back to the NotConfigured guard.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["IdentityHub:BaseAddress"] = "https://ih.example.org" })
            .Build();
        var sut = new ServiceCollection()
            .AddIdentityHubService(configuration.GetSection("IdentityHub"))
            .BuildServiceProvider();

        // Resolving the client validates the settings, so the deployment fails loudly rather than
        // quietly running on the NotConfigured guard with one key set.
        var act = () => sut.GetRequiredService<IIdentityHubService>();

        act.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain(nameof(IdentityHubSettings.UniversalResolverAddress));
    }
}
