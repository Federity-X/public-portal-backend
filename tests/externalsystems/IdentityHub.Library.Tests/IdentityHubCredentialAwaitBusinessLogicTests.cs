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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.Eclipse.TractusX.Portal.Backend.Framework.DateTimeProvider;
using Org.Eclipse.TractusX.Portal.Backend.Framework.ErrorHandling;
using Org.Eclipse.TractusX.Portal.Backend.Framework.Processes.Library.Enums;
using Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.BusinessLogic;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.DBAccess.Repositories;
using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;
using Org.Eclipse.TractusX.Portal.Backend.Processes.ApplicationChecklist.Library;
using System.Collections.Immutable;
using Xunit;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.Tests;

public class IdentityHubCredentialAwaitBusinessLogicTests
{
    private static readonly Guid ApplicationId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly IApplicationRepository _applicationRepository = A.Fake<IApplicationRepository>();
    private readonly IDateTimeProvider _dateTimeProvider = A.Fake<IDateTimeProvider>();
    private readonly IIdentityHubCredentialAwaitBusinessLogic _sut;

    public IdentityHubCredentialAwaitBusinessLogicTests()
    {
        var portalRepositories = A.Fake<IPortalRepositories>();
        A.CallTo(() => portalRepositories.GetInstance<IApplicationRepository>()).Returns(_applicationRepository);
        A.CallTo(() => _dateTimeProvider.OffsetNow).Returns(Now);

        _sut = new IdentityHubCredentialAwaitBusinessLogic(
            portalRepositories,
            _dateTimeProvider,
            Options.Create(new IdentityHubSettings { MaxCredentialWaitTimeInDays = 1 }),
            A.Fake<ILogger<IdentityHubCredentialAwaitBusinessLogic>>());
    }

    private static IApplicationChecklistService.WorkerChecklistProcessStepData Context(ProcessStepTypeId stepTypeId) =>
        new(ApplicationId, stepTypeId, ImmutableDictionary<ApplicationChecklistEntryTypeId, ApplicationChecklistEntryStatusId>.Empty, Enumerable.Empty<ProcessStepTypeId>());

    private void WaitingSince(ProcessStepTypeId stepTypeId, params DateTimeOffset[] dateCreated) =>
        A.CallTo(() => _applicationRepository.GetActiveProcessStepsDateCreated(ApplicationId, stepTypeId))
            .Returns(dateCreated.AsEnumerable());

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE)]
    public async Task WithinTheBudget_StaysInTodoAndTouchesNothing(ProcessStepTypeId stepTypeId)
    {
        // The callback is still expected to finalize this step; the handler must not compete with it.
        WaitingSince(stepTypeId, Now.AddHours(-23));

        var result = await Execute(stepTypeId);

        result.StepStatusId.Should().Be(ProcessStepStatusId.TODO);
        result.ModifyChecklistEntry.Should().BeNull();
        result.ScheduleStepTypeIds.Should().BeNull();
        result.Modified.Should().BeFalse();
        result.ProcessMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE, ProcessStepTypeId.RETRIGGER_REQUEST_MEMBERSHIP_CREDENTIAL)]
    public async Task PastTheBudget_FailsAndOffersTheRetrigger(ProcessStepTypeId stepTypeId, ProcessStepTypeId expectedRetrigger)
    {
        WaitingSince(stepTypeId, Now.AddDays(-1).AddSeconds(-1));

        var result = await Execute(stepTypeId);

        result.StepStatusId.Should().Be(ProcessStepStatusId.FAILED);
        result.ScheduleStepTypeIds.Should().ContainSingle().Which.Should().Be(expectedRetrigger);
        result.ProcessMessage.Should().Contain("within 1 day");
    }

    [Theory]
    [InlineData(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE)]
    [InlineData(ProcessStepTypeId.AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE)]
    public async Task ExactlyAtTheDeadline_KeepsWaiting(ProcessStepTypeId stepTypeId)
    {
        WaitingSince(stepTypeId, Now.AddDays(-1));

        var result = await Execute(stepTypeId);

        result.StepStatusId.Should().Be(ProcessStepStatusId.TODO);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task WithoutExactlyOneActiveStep_ThrowsConflict(int activeSteps)
    {
        WaitingSince(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE, Enumerable.Repeat(Now, activeSteps).ToArray());

        async Task Act() => await Execute(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE);

        var ex = await Assert.ThrowsAsync<ConflictException>(Act);
        ex.Message.Should().Contain(ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE.ToString());
    }

    private Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> Execute(ProcessStepTypeId stepTypeId) =>
        stepTypeId == ProcessStepTypeId.AWAIT_BPN_CREDENTIAL_RESPONSE
            ? _sut.AwaitBpnCredentialResponse(Context(stepTypeId), CancellationToken.None)
            : _sut.AwaitMembershipCredentialResponse(Context(stepTypeId), CancellationToken.None);
}
