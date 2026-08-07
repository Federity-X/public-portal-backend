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

public interface IIdentityHubBusinessLogic
{
    /// <summary>
    /// Provisions the company's managed IdentityHub holder wallet (ParticipantContext + did:web)
    /// for the application, then hands off to the existing VALIDATE_DID_DOCUMENT chain.
    /// The IdentityHub counterpart of the Custodian/DIM wallet-creation steps (BE-293).
    /// </summary>
    Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> CreateIdentityHubWalletAsync(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken);
}
