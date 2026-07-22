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
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Xunit;

namespace Org.Eclipse.TractusX.Portal.Backend.Onboarding.WalletProvider.Tests;

public class OnboardingWalletTests
{
    private static IServiceProvider BuildProvider(params (string Key, string? Value)[] config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();
        return new ServiceCollection()
            .AddLogging()
            .AddOnboardingWallet(configuration)
            .BuildServiceProvider();
    }

    [Theory]
    [InlineData("IdentityHub", WalletProviderId.IdentityHub)]
    [InlineData("Dim", WalletProviderId.Dim)]
    [InlineData("Custodian", WalletProviderId.Custodian)]
    public void GetProvider_ReturnsConfiguredProvider(string configured, WalletProviderId expected)
    {
        var sut = BuildProvider(("Onboarding:WalletProvider", configured)).GetRequiredService<IWalletProviderResolver>();

        sut.Provider.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetProvider_WithoutConfiguration_FailsFast(string? configured)
    {
        // No default: the setting this replaces (UseDimWallet) shipped as false i.e. Custodian, so
        // defaulting either way would silently move an existing deployment onto a different wallet.
        var provider = configured is null
            ? BuildProvider()
            : BuildProvider(("Onboarding:WalletProvider", configured));

        var act = () => provider.GetRequiredService<IWalletProviderResolver>();

        // The message must name the setting - this is the only thing an operator upgrading from
        // UseDimWallet has to go on.
        act.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain(nameof(OnboardingWalletSettings.WalletProvider));
    }

    [Fact]
    public void GetProvider_WithInvalidValue_FailsFast()
    {
        var provider = BuildProvider(("Onboarding:WalletProvider", "NotARealProvider"));

        var act = () => provider.GetRequiredService<IWalletProviderResolver>();

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Onboarding:WalletProvider");
    }
}
