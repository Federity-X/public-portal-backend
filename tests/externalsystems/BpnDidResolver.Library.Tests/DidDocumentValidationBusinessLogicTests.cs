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

using Org.Eclipse.TractusX.Portal.Backend.BpnDidResolver.Library.BusinessLogic;
using Org.Eclipse.TractusX.Portal.Backend.Framework.DateTimeProvider;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Processes.Library.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Onboarding.WalletProvider;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Processes.ApplicationChecklist.Library;
using System.Collections.Immutable;

namespace Org.Eclipse.TractusX.Portal.Backend.BpnDidResolver.Library.Tests;

/// <summary>
/// These cases moved verbatim from DimBusinessLogicTests when VALIDATE_DID_DOCUMENT was made
/// provider-neutral; the assertions are unchanged, only the resolver behind them is now keyed.
/// </summary>
public class DidDocumentValidationBusinessLogicTests
{
    private static readonly Guid ApplicationId = Guid.NewGuid();

    private readonly IApplicationRepository _applicationRepository;
    private readonly IDidDocumentResolver _resolver;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IDidDocumentValidationBusinessLogic _sut;

    public DidDocumentValidationBusinessLogicTests()
    {
        _applicationRepository = A.Fake<IApplicationRepository>();
        _resolver = A.Fake<IDidDocumentResolver>();
        _dateTimeProvider = A.Fake<IDateTimeProvider>();
        var portalRepositories = A.Fake<IPortalRepositories>();
        var resolverSelector = A.Fake<IDidDocumentResolverSelector>();
        var walletProviderResolver = A.Fake<IWalletProviderResolver>();

        A.CallTo(() => portalRepositories.GetInstance<IApplicationRepository>()).Returns(_applicationRepository);
        A.CallTo(() => walletProviderResolver.Provider).Returns(WalletProviderId.Dim);
        A.CallTo(() => resolverSelector.GetForProvider(A<WalletProviderId>._)).Returns(_resolver);
        A.CallTo(() => _resolver.MaxValidationTimeInDays).Returns(7);

        _sut = new DidDocumentValidationBusinessLogic(portalRepositories, resolverSelector, walletProviderResolver, _dateTimeProvider);
    }

    private static IApplicationChecklistService.WorkerChecklistProcessStepData ContextWith(ApplicationChecklistEntryStatusId identityWallet) =>
        new(
            ApplicationId,
            default,
            new Dictionary<ApplicationChecklistEntryTypeId, ApplicationChecklistEntryStatusId>
            {
                { ApplicationChecklistEntryTypeId.IDENTITY_WALLET, identityWallet },
            }.ToImmutableDictionary(),
            Enumerable.Empty<ProcessStepTypeId>());

    [Fact]
    public async Task ValidateDidDocument_WithProcessInTodo_ProcessFails()
    {
        var result = await _sut.ValidateDidDocument(ContextWith(ApplicationChecklistEntryStatusId.TO_DO), CancellationToken.None);

        result.StepStatusId.Should().Be(ProcessStepStatusId.FAILED);
    }

    [Fact]
    public async Task ValidateDidDocument_WithoutApplication_ThrowsNotFoundException()
    {
        A.CallTo(() => _applicationRepository.GetDidApplicationId(ApplicationId))
            .Returns((false, null, Enumerable.Empty<DateTimeOffset>()));

        async Task Act() => await _sut.ValidateDidDocument(ContextWith(ApplicationChecklistEntryStatusId.IN_PROGRESS), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<NotFoundException>(Act);
        ex.Message.Should().Be($"CompanyApplication {ApplicationId} does not exist");
    }

    [Fact]
    public async Task ValidateDidDocument_WitEmptyDid_ThrowsConflictException()
    {
        A.CallTo(() => _applicationRepository.GetDidApplicationId(ApplicationId))
            .Returns((true, null, Enumerable.Empty<DateTimeOffset>()));

        async Task Act() => await _sut.ValidateDidDocument(ContextWith(ApplicationChecklistEntryStatusId.IN_PROGRESS), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(Act);
        ex.Message.Should().Be("There must be a did set");
    }

    [Fact]
    public async Task ValidateDidDocument_WithoutProcess_ThrowsConflictException()
    {
        const string did = "did:web:123";
        A.CallTo(() => _applicationRepository.GetDidApplicationId(ApplicationId))
            .Returns((true, did, Enumerable.Empty<DateTimeOffset>()));
        A.CallTo(() => _resolver.ValidateDid(did, A<CancellationToken>._)).Returns(false);

        async Task Act() => await _sut.ValidateDidDocument(ContextWith(ApplicationChecklistEntryStatusId.IN_PROGRESS), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(Act);
        ex.Message.Should().Be($"There must be excatly on active {ProcessStepTypeId.VALIDATE_DID_DOCUMENT}");
    }

    [Fact]
    public async Task ValidateDidDocument_WithInvalidDid_ProcessStaysInTodo()
    {
        const string did = "did:web:123";
        var now = DateTimeOffset.UtcNow;
        A.CallTo(() => _dateTimeProvider.OffsetNow).Returns(now);
        A.CallTo(() => _applicationRepository.GetDidApplicationId(ApplicationId))
            .Returns((true, did, Enumerable.Repeat(now, 1)));
        A.CallTo(() => _resolver.ValidateDid(did, A<CancellationToken>._)).Returns(false);

        var result = await _sut.ValidateDidDocument(ContextWith(ApplicationChecklistEntryStatusId.IN_PROGRESS), CancellationToken.None);

        result.StepStatusId.Should().Be(ProcessStepStatusId.TODO);
        result.ScheduleStepTypeIds.Should().BeNull();
        result.ProcessMessage.Should().BeNull();
    }

    [Fact]
    public async Task ValidateDidDocument_WithCreatedOutsideMaxTime_ProcessFailed()
    {
        const string did = "did:web:123";
        var now = DateTimeOffset.UtcNow;
        A.CallTo(() => _dateTimeProvider.OffsetNow).Returns(now);
        A.CallTo(() => _applicationRepository.GetDidApplicationId(ApplicationId))
            .Returns((true, did, Enumerable.Repeat(now.AddDays(-8), 1)));
        A.CallTo(() => _resolver.ValidateDid(did, A<CancellationToken>._)).Returns(false);

        var result = await _sut.ValidateDidDocument(ContextWith(ApplicationChecklistEntryStatusId.IN_PROGRESS), CancellationToken.None);

        result.StepStatusId.Should().Be(ProcessStepStatusId.FAILED);
        result.ScheduleStepTypeIds.Should().ContainSingle(x => x == ProcessStepTypeId.RETRIGGER_VALIDATE_DID_DOCUMENT);
        result.ProcessMessage.Should().Be("The validation was aborted");
    }

    [Fact]
    public async Task ValidateDidDocument_WithValidDid_ProcessDone()
    {
        const string did = "did:web:123";
        var now = DateTimeOffset.Now;
        A.CallTo(() => _dateTimeProvider.OffsetNow).Returns(now);
        A.CallTo(() => _applicationRepository.GetDidApplicationId(ApplicationId))
            .Returns((true, did, Enumerable.Repeat(now, 1)));
        A.CallTo(() => _resolver.ValidateDid(did, A<CancellationToken>._)).Returns(true);

        var result = await _sut.ValidateDidDocument(ContextWith(ApplicationChecklistEntryStatusId.IN_PROGRESS), CancellationToken.None);

        result.StepStatusId.Should().Be(ProcessStepStatusId.DONE);
        result.ScheduleStepTypeIds.Should().ContainSingle().Which.Should().Be(ProcessStepTypeId.TRANSMIT_BPN_DID);
    }

    [Theory]
    [InlineData(WalletProviderId.Dim)]
    [InlineData(WalletProviderId.Custodian)]
    [InlineData(WalletProviderId.IdentityHub)]
    public async Task ValidateDidDocument_ResolvesTheDidThroughTheConfiguredProvidersResolver(WalletProviderId provider)
    {
        // The whole point of the keyed resolver: an IdentityHub deployment must reach its own
        // universal resolver without configuring a Dim section.
        const string did = "did:web:123";
        var now = DateTimeOffset.UtcNow;
        var resolverSelector = A.Fake<IDidDocumentResolverSelector>();
        var walletProviderResolver = A.Fake<IWalletProviderResolver>();
        var portalRepositories = A.Fake<IPortalRepositories>();
        A.CallTo(() => portalRepositories.GetInstance<IApplicationRepository>()).Returns(_applicationRepository);
        A.CallTo(() => walletProviderResolver.Provider).Returns(provider);
        A.CallTo(() => resolverSelector.GetForProvider(provider)).Returns(_resolver);
        A.CallTo(() => _dateTimeProvider.OffsetNow).Returns(now);
        A.CallTo(() => _applicationRepository.GetDidApplicationId(ApplicationId))
            .Returns((true, did, Enumerable.Repeat(now, 1)));
        A.CallTo(() => _resolver.ValidateDid(did, A<CancellationToken>._)).Returns(true);
        var sut = new DidDocumentValidationBusinessLogic(portalRepositories, resolverSelector, walletProviderResolver, _dateTimeProvider);

        var result = await sut.ValidateDidDocument(ContextWith(ApplicationChecklistEntryStatusId.IN_PROGRESS), CancellationToken.None);

        result.StepStatusId.Should().Be(ProcessStepStatusId.DONE);
        A.CallTo(() => resolverSelector.GetForProvider(provider)).MustHaveHappenedOnceExactly();
    }
}
