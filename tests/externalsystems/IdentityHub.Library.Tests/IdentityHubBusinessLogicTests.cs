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
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Processes.Library.Enums;
using Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.BusinessLogic;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Models;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Entities;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Processes.ApplicationChecklist.Library;
using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.Tests;

public class IdentityHubBusinessLogicTests
{
    private static readonly Guid ApplicationId = Guid.NewGuid();
    private const string Bpn = "BPNL000000000001";
    private const string Did = "did:web:ih.example.org:BPNL000000000001";

    private readonly IApplicationRepository _applicationRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly IIdentityHubService _identityHubService;
    private readonly IdentityHubBusinessLogic _sut;

    public IdentityHubBusinessLogicTests()
    {
        _applicationRepository = A.Fake<IApplicationRepository>();
        _companyRepository = A.Fake<ICompanyRepository>();
        var portalRepositories = A.Fake<IPortalRepositories>();
        _identityHubService = A.Fake<IIdentityHubService>();

        A.CallTo(() => portalRepositories.GetInstance<IApplicationRepository>()).Returns(_applicationRepository);
        A.CallTo(() => portalRepositories.GetInstance<ICompanyRepository>()).Returns(_companyRepository);

        _sut = new IdentityHubBusinessLogic(portalRepositories, _identityHubService);
    }

    private static IApplicationChecklistService.WorkerChecklistProcessStepData Context(
        ApplicationChecklistEntryStatusId bpn, ApplicationChecklistEntryStatusId registration) =>
        new(
            ApplicationId,
            default,
            new Dictionary<ApplicationChecklistEntryTypeId, ApplicationChecklistEntryStatusId>
            {
                { ApplicationChecklistEntryTypeId.BUSINESS_PARTNER_NUMBER, bpn },
                { ApplicationChecklistEntryTypeId.REGISTRATION_VERIFICATION, registration },
                { ApplicationChecklistEntryTypeId.IDENTITY_WALLET, ApplicationChecklistEntryStatusId.TO_DO },
            }.ToImmutableDictionary(),
            Enumerable.Empty<ProcessStepTypeId>());

    [Theory]
    [InlineData(ApplicationChecklistEntryStatusId.FAILED, ApplicationChecklistEntryStatusId.DONE)]
    [InlineData(ApplicationChecklistEntryStatusId.DONE, ApplicationChecklistEntryStatusId.FAILED)]
    public async Task CreateIdentityHubWalletAsync_WithFailedPrecondition_Skips(
        ApplicationChecklistEntryStatusId bpn, ApplicationChecklistEntryStatusId registration)
    {
        var result = await _sut.CreateIdentityHubWalletAsync(Context(bpn, registration), CancellationToken.None);

        result.StepStatusId.Should().Be(ProcessStepStatusId.SKIPPED);
        result.ScheduleStepTypeIds.Should().BeNull();
        A.CallTo(() => _identityHubService.CreateHolderWalletAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task CreateIdentityHubWalletAsync_WithBpnPending_DoesNothing()
    {
        var result = await _sut.CreateIdentityHubWalletAsync(
            Context(ApplicationChecklistEntryStatusId.TO_DO, ApplicationChecklistEntryStatusId.DONE), CancellationToken.None);

        result.StepStatusId.Should().Be(ProcessStepStatusId.TODO);
        result.Modified.Should().BeFalse();
        result.ScheduleStepTypeIds.Should().BeNull();
        A.CallTo(() => _identityHubService.CreateHolderWalletAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task CreateIdentityHubWalletAsync_WithValid_CreatesManagedWalletAndSchedulesValidateDid()
    {
        // Arrange
        var companyId = Guid.NewGuid();
        var company = new Company(companyId, "Test Corp", CompanyStatusId.ACTIVE, DateTimeOffset.UtcNow);
        var didDocument = JsonSerializer.SerializeToDocument(new { id = Did });
        A.CallTo(() => _applicationRepository.GetCompanyAndApplicationDetailsForCreateWalletAsync(ApplicationId))
            .Returns((companyId, "Test Corp", Bpn));
        A.CallTo(() => _identityHubService.CreateHolderWalletAsync(Bpn, "Test Corp", A<CancellationToken>._))
            .Returns((Did, didDocument));
        A.CallTo(() => _companyRepository.AttachAndModifyCompany(companyId, A<Action<Company>>._, A<Action<Company>>._))
            .Invokes((Guid _, Action<Company>? initialize, Action<Company> modify) =>
            {
                initialize?.Invoke(company);
                modify(company);
            });

        // Act
        var result = await _sut.CreateIdentityHubWalletAsync(
            Context(ApplicationChecklistEntryStatusId.DONE, ApplicationChecklistEntryStatusId.DONE), CancellationToken.None);

        // Assert
        result.StepStatusId.Should().Be(ProcessStepStatusId.DONE);
        result.ScheduleStepTypeIds.Should().ContainSingle().Which.Should().Be(ProcessStepTypeId.VALIDATE_DID_DOCUMENT);
        // A Portal-managed wallet must NOT be tagged as bring-your-own-wallet.
        A.CallTo(() => _companyRepository.CreateCustomerWallet(companyId, Did, didDocument, BringYourOwnWalletClientFields.NotUsed))
            .MustHaveHappenedOnceExactly();
        company.DidDocumentLocation.Should().Be(Did);

        var entry = new ApplicationChecklistEntry(ApplicationId, ApplicationChecklistEntryTypeId.IDENTITY_WALLET, ApplicationChecklistEntryStatusId.TO_DO, DateTimeOffset.UtcNow);
        result.ModifyChecklistEntry.Should().NotBeNull();
        result.ModifyChecklistEntry!.Invoke(entry);
        entry.ApplicationChecklistEntryStatusId.Should().Be(ApplicationChecklistEntryStatusId.IN_PROGRESS);
    }

    [Fact]
    public async Task CreateIdentityHubWalletAsync_WithNotSubmittedApplication_ThrowsConflict()
    {
        A.CallTo(() => _applicationRepository.GetCompanyAndApplicationDetailsForCreateWalletAsync(ApplicationId))
            .Returns(new ValueTuple<Guid, string, string?>());

        async Task Act() => await _sut.CreateIdentityHubWalletAsync(
            Context(ApplicationChecklistEntryStatusId.DONE, ApplicationChecklistEntryStatusId.DONE), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(Act);
        ex.Message.Should().Be($"CompanyApplication {ApplicationId} is not in status SUBMITTED");
        A.CallTo(() => _identityHubService.CreateHolderWalletAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task CreateIdentityHubWalletAsync_WithEmptyBpn_ThrowsConflict()
    {
        var companyId = Guid.NewGuid();
        A.CallTo(() => _applicationRepository.GetCompanyAndApplicationDetailsForCreateWalletAsync(ApplicationId))
            .Returns(new ValueTuple<Guid, string, string?>(companyId, "Test Corp", null));

        async Task Act() => await _sut.CreateIdentityHubWalletAsync(
            Context(ApplicationChecklistEntryStatusId.DONE, ApplicationChecklistEntryStatusId.DONE), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(Act);
        ex.Message.Should().Be($"BusinessPartnerNumber (bpn) for CompanyApplication {ApplicationId} company {companyId} is empty");
        A.CallTo(() => _identityHubService.CreateHolderWalletAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }
}
