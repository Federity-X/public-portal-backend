<!--
- Copyright (c) 2026 Contributors to the Eclipse Foundation
-
- See the NOTICE file(s) distributed with this work for additional
- information regarding copyright ownership.
-
- This program and the accompanying materials are made available under the
- terms of the Apache License, Version 2.0 which is available at
- https://www.apache.org/licenses/LICENSE-2.0.
-
- Unless required by applicable law or agreed to in writing, software
- distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
- WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
- License for the specific language governing permissions and limitations
- under the License.
-
- SPDX-License-Identifier: Apache-2.0
-->

# IdentityHub as the onboarding wallet

The onboarding checklist provisions the applicant's wallet and credentials through one of three
providers, selected per deployment by a single setting.

## Selecting a provider

`Onboarding:WalletProvider` is **required** and has no default. Every deployable that participates in
onboarding — Administration, Registration and Processes.Worker — binds it via `AddOnboardingWallet`
and fails to start if it is unset.

| Value | Wallet | Credentials |
| --- | --- | --- |
| `Custodian` | Managed identity wallet (MiW) | none — the checklist omits the BPNL/Membership entries |
| `Dim` | SAP DIM | issued by the DIM/ssi issuer-component on the holder's behalf |
| `IdentityHub` | Tractus-X IdentityHub, Portal-managed holder | requested by the holder itself from the IssuerService over DCP |

This replaced the per-feature `UseDimWallet` flags that were duplicated across five settings classes.
It is required rather than defaulted on purpose: `UseDimWallet` shipped as `false` (i.e. Custodian), so
any default would silently move some existing deployment onto a different wallet on upgrade.

## Checklist flow for `IdentityHub`

```
CREATE_IDENTITY_HUB_WALLET      creates + activates the holder ParticipantContext, persists its
                                did:web on the company (tagged NOT_USED, not bring-your-own-wallet)
VALIDATE_DID_DOCUMENT           polls IdentityHub:UniversalResolverAddress until the did:web resolves
TRANSMIT_BPN_DID                registers did + bpn with BDRS
REQUEST_BPN_CREDENTIAL          registers the holder with the IssuerService, then POSTs
                                credentials/request; no technical-user credentials are involved
AWAIT_BPN_CREDENTIAL_RESPONSE   waits for the holder-side callback (below)
REQUEST_/AWAIT_MEMBERSHIP_CREDENTIAL
START_CLEARING_HOUSE -> ... -> APPLICATION_ACTIVATION
```

`VALIDATE_DID_DOCUMENT` is provider-neutral: it resolves through the `IDidDocumentResolver` keyed to
the configured provider, so an IdentityHub deployment needs **no** `Dim` configuration section.

Use-case **framework** credentials are out of scope for this wallet;
`companydata/useCaseParticipation` returns a 409 when `WalletProvider=IdentityHub`.

## Credential completion is driven from the IdentityHub side

The Portal does not poll for credential issuance. The `portal-credential-callback` extension in
[Federity-X/public-tractusx-identityhub](https://github.com/Federity-X/public-tractusx-identityhub)
watches the `HolderCredentialRequestStore` for requests reaching a terminal state and posts the
Portal's existing BPN-keyed issuer endpoints, which advance the `AWAIT_*_CREDENTIAL_RESPONSE` steps.

**This extension must be deployed and configured, or applications stall at
`AWAIT_BPN_CREDENTIAL_RESPONSE` with no error anywhere.** Recovery is
`POST /api/administration/registration/application/{applicationId}/retrigger-bpn-credential`
(and `.../retrigger-membership-credential`).

### Contract between the two repositories

| Concern | Portal side | Extension side |
| --- | --- | --- |
| Endpoints | `POST /api/administration/registration/issuer/{bpn,membership}credential` | `tx.portal.callback.base.url` + the same suffixes |
| Body | `IssuerResponseData { bpn, status, message }` | same |
| `status` encoding | **string only** — `JsonStringEnumConverter(allowIntegerValues: false)`; `1`/`2` are rejected | sends `SUCCESSFUL` / `UNSUCCESSFUL` |
| BPN | upper-cased on receipt (`GetApplicationIdByBpn`) | recovered lower-cased from `participantContextId` |
| Auth | roles `update_application_bpn_credential`, `update_application_membership_credential` | OAuth2 client-credentials (`tx.portal.callback.token.url`, `.client.id`, `.client.secret`, `.scope`) |
| Credential types | `IdentityHub:BpnCredentialType`, `IdentityHub:MembershipCredentialType` | `tx.portal.callback.bpn.credential.type`, `.membership.credential.type` |

> **Keep the credential-type strings in sync.** The extension matches the requested VC type against its
> configuration with an exact `equals` and **silently skips** requests that do not match. A mismatch
> produces no error on either side — the application simply never leaves
> `AWAIT_*_CREDENTIAL_RESPONSE`. Both sides default to `BpnCredential` / `MembershipCredential`.

## Configuration

`Onboarding:WalletProvider` is top-level. The rest lives under `ApplicationChecklist:IdentityHub` in
Processes.Worker and Administration (see their `appsettings.json` for the full key list).

| Key | Notes |
| --- | --- |
| `BaseAddress` | IdentityHub Identity (admin) API |
| `ApiKey` | super-user key, sent as `x-api-key`. **Secret** |
| `DidDocumentBaseLocation` | the DID is `did:web:{DidDocumentBaseLocation}:{BPN}`; must be resolvable and registered in BDRS |
| `UniversalResolverAddress` | used by `VALIDATE_DID_DOCUMENT` |
| `MaxValidationTimeInDays` | how long the DID may stay unresolvable before the step fails for retrigger |
| `CredentialServiceBaseAddress` | baked into the holder's `CredentialService` service endpoint |
| `IssuerDid`, `IssuerAdminBaseAddress`, `IssuerAdminApiKey`, `IssuerParticipantId` | IssuerService. `IssuerParticipantId` goes **plain** into the URL path, not base64 (EDC 0.17.0 / IdentityHub #937) — a base64 value yields 404. **`IssuerAdminApiKey` is secret** |
| `BpnCredentialType`/`BpnCredentialDefinitionId`, `MembershipCredentialType`/`MembershipCredentialDefinitionId` | see the sync warning above |

All are `[Required]` and validated at startup **when the section is present**. When the section is
absent entirely, a guard is registered instead, so a `WalletProvider=IdentityHub` deployment that
forgot the section fails with an actionable configuration error rather than a DNS error.

### Not retained: the holder's STS client secret

The IdentityHub returns the holder's STS `clientSecret` once, on the `201` from participant-context
creation. The Portal deliberately does not read or store it: onboarding covers wallet, credentials and
checklist progression, not making the company a data-exchange peer. A company that later runs its own
connector needs that secret in its own vault (`edc-wallet-secret`), which is a separate step outside
this flow.
