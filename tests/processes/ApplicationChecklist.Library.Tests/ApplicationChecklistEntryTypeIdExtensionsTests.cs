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

using Org.Eclipse.TractusX.Portal.Backend.PortalBackend.PortalEntities.Enums;

namespace Org.Eclipse.TractusX.Portal.Backend.Processes.ApplicationChecklist.Library.Tests;

public class ApplicationChecklistEntryTypeIdExtensionsTests
{
    /// <summary>
    /// RegistrationController exposes a retrigger endpoint per (entry type, retrigger step) pair, and
    /// RegistrationBusinessLogic.TriggerChecklistAsync validates the pair against this map before doing
    /// anything. A pair missing here makes its endpoint permanently return 400 - which is how
    /// retrigger-bpn-credential and retrigger-membership-credential went unusable.
    /// </summary>
    [Theory]
    [InlineData(ApplicationChecklistEntryTypeId.BPNL_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_BPN_CREDENTIAL, ProcessStepTypeId.REQUEST_BPN_CREDENTIAL)]
    [InlineData(ApplicationChecklistEntryTypeId.MEMBERSHIP_CREDENTIAL, ProcessStepTypeId.RETRIGGER_REQUEST_MEMBERSHIP_CREDENTIAL, ProcessStepTypeId.REQUEST_MEMBERSHIP_CREDENTIAL)]
    [InlineData(ApplicationChecklistEntryTypeId.IDENTITY_WALLET, ProcessStepTypeId.RETRIGGER_CREATE_IDENTITY_HUB_WALLET, ProcessStepTypeId.CREATE_IDENTITY_HUB_WALLET)]
    [InlineData(ApplicationChecklistEntryTypeId.IDENTITY_WALLET, ProcessStepTypeId.RETRIGGER_CREATE_DIM_WALLET, ProcessStepTypeId.CREATE_DIM_WALLET)]
    public void RetriggerStep_IsManuallyTriggerable_AndMapsToItsStep(
        ApplicationChecklistEntryTypeId entryTypeId, ProcessStepTypeId retriggerStep, ProcessStepTypeId expectedNextStep)
    {
        entryTypeId.GetManualTriggerProcessStepIds().Should().Contain(retriggerStep);

        var (nextStep, status) = retriggerStep.GetNextProcessStepDataForManualTriggerProcessStepId();

        nextStep.Should().Be(expectedNextStep);
        status.Should().Be(ApplicationChecklistEntryStatusId.TO_DO);
    }

    [Fact]
    public void EveryManuallyTriggerableStep_HasANextStepMapping()
    {
        // TriggerChecklistAsync throws UnexpectedConditionException when a step is listed as triggerable
        // but has no mapping, so the two tables must not drift apart.
        var unmapped = Enum.GetValues<ApplicationChecklistEntryTypeId>()
            .SelectMany(entryTypeId => entryTypeId.GetManualTriggerProcessStepIds())
            .Distinct()
            .Where(stepTypeId => stepTypeId.GetNextProcessStepDataForManualTriggerProcessStepId() == default)
            .ToList();

        unmapped.Should().BeEmpty();
    }
}
