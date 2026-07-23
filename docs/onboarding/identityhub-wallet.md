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
`AWAIT_BPN_CREDENTIAL_RESPONSE` with no error anywhere.**

### The Portal polls; the callback is the fast path

For **`WalletProvider=IdentityHub`**, each worker cycle the `AWAIT_*_CREDENTIAL_RESPONSE` steps read the
holder's request state directly from the IdentityHub:

```
GET {IdentityHub:BaseAddress}/v1alpha/participants/{lowercased-bpn}/credentials/request/{holderPid}
    x-api-key: {IdentityHub:ApiKey}          holderPid = {lowercased-bpn}-{lowercased credential type}
```

| `status` | Portal |
| --- | --- |
| `ISSUED` | advance the checklist exactly as the callback would — **no callback required** |
| `ERROR` | fail + retrigger. The DTO carries no `errorDetail`, so the reason is only in the holder's logs |
| `CREATED`, `REQUESTING`, `REQUESTED`, or `404` | keep waiting, until the deadline below |

This makes the Portal authoritative. The `portal-credential-callback` extension is still worth deploying
— it advances onboarding in seconds rather than on the worker's polling interval — but it is no longer on
the critical path, so a lost callback degrades onboarding to *slower* rather than *stuck*.

It also closes a dead end: `holderPid` is deterministic **and** is the primary key of the holder request
store, so a retrigger re-POSTs a colliding request that changes nothing. Reading the state needs no new
request, so an already-`ISSUED` credential is now discoverable.

The lookup is a direct primary-key read and terminal requests are retained indefinitely, so a poll
reliably finds a completion the callback missed.

### The wait is bounded

`AWAIT_BPN_CREDENTIAL_RESPONSE` and `AWAIT_MEMBERSHIP_CREDENTIAL_RESPONSE` are advanced by the issuer
callback rather than by the worker, so by default they wait forever. For **`WalletProvider=IdentityHub`
only**, the worker also gives them a deadline of `IdentityHub:MaxCredentialWaitTimeInDays` (default `1`):

- within the budget the step stays `TODO` and the callback finalizes it exactly as before — the deadline
  never competes with the happy path
- past the budget the step is `FAILED` with `No … credential response was received within N day(s)`,
  a `WARN` naming the likely causes, and `RETRIGGER_REQUEST_*_CREDENTIAL` scheduled

DIM and Custodian are unaffected: their issuer posts its own callback, so their executable step set is
unchanged.

### Diagnosing a failed credential step

The deadline and the holder's own `ERROR` cover **disjoint** failure domains — confirmed against the EDC
0.17.0 holder state machine (`CredentialRequestManagerImpl` / `CredentialWriterImpl`) — so **how long the
step took to fail tells you which one you are in**:

| What went wrong | Holder state | What arrives | Step fails | Where to look |
| --- | --- | --- | --- | --- |
| Holder could not send its request (STS, endpoint, DCP) | `ERROR` — immediate and terminal, never retried | `UNSUCCESSFUL` | within **seconds** | IdentityHub holder logs |
| **Issuer accepted but never delivered** | `REQUESTED` forever | **nothing** | after the **deadline** | IssuerService |
| Callback extension absent, unreachable, or credential type mismatched | `ISSUED` | **nothing** | after the **deadline** | extension config, and the `Requesting …` line in the worker log for the type actually sent |
| Success | `ISSUED` | `SUCCESSFUL` | — | — |

The second row is why this deadline exists at all: an issuer-side give-up is **invisible to the holder** —
there is no inbound `ERROR` transition and no holder-side `REQUESTED` timeout — so nothing is ever posted
and no amount of extension hardening would surface it. The deadline is the only mechanism that catches it.

A holder `ERROR` fires within seconds of a send failure and the success path completes in minutes, so a
step that fails on the deadline is never a slow-but-healthy issuance — unless a deployment runs long
attestation pipelines, in which case raise `MaxCredentialWaitTimeInDays`.

### Recovering a credential step

Retrigger is the **canonical recovery** for both IdentityHub-originated outcomes — an application stuck
awaiting a callback that never arrived, and a `BPNL_CREDENTIAL`/`MEMBERSHIP_CREDENTIAL` entry driven to
`FAILED` by a terminal `ERROR` from the IssuerService:

```
POST /api/administration/registration/application/{applicationId}/retrigger-bpn-credential
POST /api/administration/registration/application/{applicationId}/retrigger-membership-credential
```

Each resets its checklist entry to `TO_DO` and re-schedules `REQUEST_*_CREDENTIAL`, so the holder
requests the credential again from scratch. Both require the `approve_new_partner` role.

> Both endpoints returned `400` unconditionally until this change — `BPNL_CREDENTIAL` and
> `MEMBERSHIP_CREDENTIAL` had no entry in the manual-trigger map. `ApplicationChecklistEntryTypeIdExtensionsTests`
> now pins the mapping, including a case asserting that every manually triggerable step has a next-step
> mapping, so the pair cannot silently drift apart again.

Diagnosing from Portal logs: every inbound issuer callback is logged with its BPN and status before it
is resolved, and every outbound credential request is logged with the credential type actually sent.
Between them, a stalled application can be told apart from a mismatched credential type without reading
the IdentityHub's logs.

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

### Use the seeded `sa-cl24-01` technical user

No new Keycloak client is needed. `Seeder/Data/technical_users.json` already seeds **`sa-cl24-01`** —
*"Technical User for the Connection between the SSI Credential Issuer and the Portal"* — holding exactly
`update_application_bpn_credential` and `update_application_membership_credential`. Point
`tx.portal.callback.client.id` at it rather than minting a new client, so the callback keeps the least
privilege the seed already defines.

### The Portal is safe under replayed callbacks

The extension deduplicates in memory only, so a restart or rolling upgrade replays its whole retained
terminal-state store. That is non-destructive here, and deliberately so:

| Replay arrives… | Portal response | Effect |
| --- | --- | --- |
| after onboarding completed | `404` — no application in `SUBMITTED` for that BPN | none |
| when the step already advanced | `409` — checklist entry "not eligible to run" | none |
| while the step is genuinely awaiting | `204` | advances, as intended |

Neither `404` nor `409` mutates state, so replay cannot corrupt an application. The extension must
treat both as terminal rather than retryable — it has no backoff or attempt cap, so a response class it
considers a failure re-fires every interval indefinitely.

## Configuration

`Onboarding:WalletProvider` is top-level. The rest lives under `ApplicationChecklist:IdentityHub` in
Processes.Worker and Administration (see their `appsettings.json` for the full key list).

| Key | Notes |
| --- | --- |
| `BaseAddress` | IdentityHub Identity API. Must be the **admin** ingress — the public host answers `405` to the participant-context `POST` |
| `ApiKey` | super-user key, sent as `x-api-key`. **Secret** |
| `DidDocumentBaseLocation` | the DID is `did:web:{DidDocumentBaseLocation}:{BPN}`; must be resolvable and registered in BDRS |
| `UniversalResolverAddress` | used by `VALIDATE_DID_DOCUMENT`. Must answer `GET {address}/1.0/identifiers/{urlEncodedDid}` with a DIF resolution result — i.e. `200` plus `didResolutionMetadata.error` while the DID is unpublished, and no `error` once it resolves. Any in-cluster `did:web` resolver shim has to implement that shape; it is the same call the DIM resolver makes |
| `MaxValidationTimeInDays` | how long the DID may stay unresolvable before the step fails for retrigger |
| `MaxCredentialWaitTimeInDays` | how long an `AWAIT_*_CREDENTIAL_RESPONSE` may wait for the callback before failing for retrigger. **Optional, defaults to `1`** — unlike the keys above it has a usable default, because a deployment that never tunes it still needs the deadline |
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
