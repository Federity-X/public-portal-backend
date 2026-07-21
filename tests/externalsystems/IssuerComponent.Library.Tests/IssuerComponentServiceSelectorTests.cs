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
using Microsoft.Extensions.DependencyInjection;
using Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Service;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Xunit;

namespace Org.Eclipse.TractusX.Portal.Backend.IssuerComponent.Library.Tests;

public class IssuerComponentServiceSelectorTests
{
    private readonly IReadOnlyDictionary<WalletProviderId, IIssuerComponentService> _byProvider = new Dictionary<WalletProviderId, IIssuerComponentService>
    {
        [WalletProviderId.Dim] = A.Fake<IIssuerComponentService>(),
        [WalletProviderId.Custodian] = A.Fake<IIssuerComponentService>(),
        [WalletProviderId.IdentityHub] = A.Fake<IIssuerComponentService>(),
    };

    [Theory]
    [InlineData(WalletProviderId.Dim)]
    [InlineData(WalletProviderId.Custodian)]
    [InlineData(WalletProviderId.IdentityHub)]
    public void GetForProvider_ResolvesTheKeyedServiceForThatProvider(WalletProviderId provider)
    {
        var services = new ServiceCollection();
        foreach (var (key, instance) in _byProvider)
        {
            services.AddKeyedSingleton(key, instance);
        }

        var sut = new IssuerComponentServiceSelector(services.BuildServiceProvider());

        sut.GetForProvider(provider).Should().BeSameAs(_byProvider[provider]);
    }

    [Fact]
    public void GetForProvider_WithNoKeyedRegistration_Throws()
    {
        var sut = new IssuerComponentServiceSelector(new ServiceCollection().BuildServiceProvider());

        var act = () => sut.GetForProvider(WalletProviderId.IdentityHub);

        act.Should().Throw<InvalidOperationException>();
    }
}
