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

namespace Org.Eclipse.TractusX.Portal.Backend.BpnDidResolver.Library.BusinessLogic;

public interface IDidDocumentValidationBusinessLogic
{
    /// <summary>
    /// Polls the universal resolver of the configured wallet provider until the holder's did:web
    /// resolves, then hands off to TRANSMIT_BPN_DID. Provider-neutral: which resolver is used is
    /// decided by the deployment's wallet provider, not by the wallet that created the DID.
    /// </summary>
    Task<IApplicationChecklistService.WorkerChecklistProcessStepExecutionResult> ValidateDidDocument(IApplicationChecklistService.WorkerChecklistProcessStepData context, CancellationToken cancellationToken);
}
