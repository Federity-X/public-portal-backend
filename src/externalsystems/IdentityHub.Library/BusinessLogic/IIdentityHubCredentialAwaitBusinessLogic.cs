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
/// the DIM issuer that is acceptable — it posts the callback itself. For IdentityHub the callback comes
/// from a separately deployed extension (portal-credential-callback), which can be absent, misconfigured
/// or silently skipping a mismatched credential type; the application then parks forever with no error
/// anywhere. These handlers give that wait a deadline, after which the step fails and offers the existing
/// RETRIGGER_REQUEST_*_CREDENTIAL recovery.
/// </para>
/// </summary>
public interface IIdentityHubCredentialAwaitBusinessLogic
{
    Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> AwaitBpnCredentialResponse(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken);

    Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> AwaitMembershipCredentialResponse(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken);
}
