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

using Org.Eclipse.TractusX.Portal.Backend.Processes.ApplicationChecklist.Library;

namespace Org.Eclipse.TractusX.Portal.Backend.IdentityHub.Library.BusinessLogic;

/// <summary>
/// Bounds the wait on the holder-side credential callback.
/// <para>
/// AWAIT_BPN_CREDENTIAL_RESPONSE and AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE are advanced by the Portal's
/// issuer callback endpoints, not by the worker, so without a handler they sit in TODO indefinitely. For
/// the DIM issuer that is acceptable — it posts the callback itself. These handlers give that wait a
/// deadline for the IdentityHub provider, after which the step fails and offers the existing
/// RETRIGGER_REQUEST_*_CREDENTIAL recovery.
/// </para>
/// <para>
/// The deadline and the holder's own ERROR callback cover <b>disjoint</b> failure domains, confirmed
/// against the EDC 0.17.0 holder state machine:
/// <list type="bullet">
/// <item>the holder fails to send its request (STS, endpoint, DCP) — it transitions to ERROR immediately
///   and terminally, the extension posts UNSUCCESSFUL, and the step fails within seconds. Not this.</item>
/// <item><b>the issuer accepts the request and never delivers</b> — the holder stays in REQUESTED forever.
///   That state is invisible to the holder: there is no inbound ERROR transition and no holder-side
///   REQUESTED timeout, so nothing is ever posted. This deadline is the only mechanism that catches it.</item>
/// <item>the callback extension is absent, misconfigured, or silently skipping a credential type whose
///   string does not match ours — again nothing is ever posted.</item>
/// </list>
/// </para>
/// </summary>
public interface IIdentityHubCredentialAwaitBusinessLogic
{
    Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> AwaitBpnCredentialResponse(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken);

    Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> AwaitMembershipCredentialResponse(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken);
}
