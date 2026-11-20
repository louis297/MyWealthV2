---
title: Simulator
status: accepted
phase: 2
language: en
owner: ""
created: 2026-11-19
last_updated: 2026-11-20
related:
  - ../custodial-roadmap.md
  - ../adr/0021-outside-parties-behind-ports.md
  - ../adr/0013-roles-authorization-single-user-table.md
  - ../adr/0014-openiddict-authorization-code-pkce.md
  - ../adr/0008-schema-sql-as-source-of-truth.md
  - identity-auth.md
  - production-hosting.md
  - ../deployment.md
  - ../architecture.md
  - ../database-design.md
  - ../domain-model.md
  - ../api-design.md
  - ../glossary.md
  - deposit-matching.md
  - withdrawals.md
---

# Simulator

New host `simulator` (`src/Simulator`) that plays the outside parties of [custodial-roadmap.md](../custodial-roadmap.md): bank, broker, custodian, issuer. This spec is roadmap step 2, item **S0**: the skeleton with no party in it yet. Each party is added later with the flow that needs it (S1 bank with B3–B7, and so on), as an amendment to this file. The decision behind it is [ADR 0021](../adr/0021-outside-parties-behind-ports.md) (accepted 2026-11-19).

The skeleton has a control surface for a person (sign-in, Scalar, clock, reset, a party outage switch, a log) and a party surface for the platform (one status route behind a machine credential). On the platform side it adds a fifth role, `SimulatorOperator`, a fourth OIDC client `simulator` on `identity`, and the deploy entries. Scripts: platform `0036_users_role_simulator_operator.sql`; simulator `database/simulator/0001`, `0002`. No new `webapi` route or policy. No platform port: the first one lands with roadmap B3.

**Status is `accepted` (2026-11-19), landed 2026-11-19.** The twelve points of §11 were confirmed as recommended. The amendments it makes to other documents (deployment, identity-auth, ADR 0013, architecture, database-design, domain-model, api-design, glossary) were written in the same change and are now landed.

Where the build had to choose, it is recorded in §11 "Settled while landing". One rule changed: R23 resume, see point 13 there.

---

## 1. Summary

The simulator is its own process with its own database. It never reads platform tables, and the platform does not know it is there (ADR 0021 rules 2, 21, 22). In this slice it holds only a **simulator world**: an id, a seed, a **simulator clock** and an outage switch. A person with the new role `SimulatorOperator` signs in through `identity` and drives it from Scalar. The platform will call it on a separate port with a machine credential; in this slice that port answers one status route.

The rule that distinguishes this slice: the two surfaces never mix. Control routes answer only on the control port behind the sign-in; party routes answer only on the party port behind the machine credential; Caddy forwards only the control port.

---

## 2. Scope

**In**

- Host: project `src/Simulator`, Aspire resource `simulator`, ASP.NET Core minimal API. References `ServiceDefaults`, `Shared` and the new schema library (below). No reference to `Domain`, `Application`, `Infrastructure`, `Web` or any other host
- Schema library: the schema applicator (`SchemaApplicator`, `ISchemaScriptSource`, `FileSchemaScriptSource`, the version store and the batch executor) moves from `src/Infrastructure/Data/Schema` into a new project `src/Schema`, with the version store and executor taking a connection string instead of `ApplicationDbContext`. `Infrastructure` references it. `webapi` and `worker` keep running it for `MyWealthDbV2` ([ADR 0008](../adr/0008-schema-sql-as-source-of-truth.md) amendment 2026-11-19). Behaviour unchanged
- Simulator database `MyWealthSimulatorDb`, scripts in `database/simulator/` (`0001_schema_versions.sql`, `0002_world.sql`), applied by the simulator at start-up
- Control surface (port `8080`): OIDC sign-in, Scalar, `GET /control/world`, clock advance / pause / resume, party outage on / off, reset, `GET /control/log`
- Party surface (port `8082`): machine credential check, `GET /party/status`, outage answer
- Platform: `UserRole.SimulatorOperator = 4`; `User` factory; `LoginEligibility`; script `0036`; `webapi` refuses the role (401); bootstrap from configuration and Development seed; `identity` client `simulator`, allow-list row, identity cookie scheme, hosted login without tenantCode
- AppHost and TestAppHost entries; deploy entries (compose service, Caddy site, `.env` keys, image, Data Protection folder, runbook)
- Tests listed in §10, in a new project `tests/Simulator.Tests` and the existing platform test projects

**Out**

- Every outside party (bank, broker, custodian, issuer): S1–S4, each with its flow
- Every platform port, adapter, feed cursor table or `worker` job: from B3 (ADR 0021 rule 1)
- Named scenarios and the scenario library: with S1, when the first step kinds exist (ADR 0021 rule 20)
- Any read of platform data by the simulator, including instruments and reference prices (§11 point 8)
- A Blazor page set for the control surface (Scalar only in this slice)
- Managing `SimulatorOperator` people through an API or a page (bootstrap and seed only; §11 point 1)
- A push from the simulator to the platform (ADR 0021 rule 5)
- Business calendars ("next business day", T+2): with S2
- Any change to the four existing roles, their permissions or their clients

---

## 3. Stories

1. As the owner I sign in to the simulator at `sim.<domain>` with the simulator operator account, and nobody else can.
2. As the simulator operator I see the simulator world: its id, seed, the simulator's time, whether the clock is paused, whether the party surface is in an outage.
3. As the simulator operator I move the simulator clock ahead by a duration or to a later instant, and pause and resume it, so outside dates can jump ("to T+2", "to month end").
4. As the simulator operator I reset the world with a chosen seed, so a demo starts from a known state.
5. As the simulator operator I switch a party outage on, so I can show that the platform's pull jobs survive a missing party.
6. As the simulator operator I read a log of every control action with who did it, real time and simulator time.
7. As the platform (from B3) I call the party surface with my machine credential and get 401 without it, and 503 during an outage.
8. As a SystemAdmin, TenantAdmin, Adviser or Customer I cannot sign in to the simulator. As a simulator operator I cannot sign in to any portal or Back Office, and my token works on no `webapi` route.

---

## 4. Rules

### 4.1 The role and sign-in (platform)

| ID | Rule |
| --- | --- |
| R1 | **Fifth role.** `UserRole.SimulatorOperator = 4`. A platform-level role like SystemAdmin: no tenant, no adviser (`CK_Users_RoleShape`). Its email is unique among platform-level people (the existing `UQ_Users_SystemAdmin_Email` index, `WHERE TenantId IS NULL`), so it cannot share an email with a SystemAdmin. Role cannot change after create (ADR 0013 decision 5). |
| R2 | **No permission.** `RolePermissions` grants `SimulatorOperator` no permission. |
| R3 | **No `webapi` caller.** The current-user load on `webapi` treats a token whose person has role `SimulatorOperator` as unauthenticated: 401 on every route, the ones with only default `.RequireAuthorization()` (`GET /currencies`) included. Never 403, so the role cannot be probed through policies. |
| R4 | **Login eligibility.** `LoginEligibility.CanComplete` treats `SimulatorOperator` like SystemAdmin: Active, with an identity link, no tenant. |
| R5 | **Hosted login without tenantCode.** With no tenantCode, hosted login looks the person up by email among platform-level people (role SystemAdmin or SimulatorOperator, `TenantId IS NULL`). Order of checks, uniform failure copy, lockout and the one-hash rule are unchanged (identity-auth amendment 2026-11-13, lockout). |
| R6 | **Client `simulator`.** A fourth confidential client on `identity`: authorization code + PKCE, scopes `openid`, `profile`, `email` only. No `offline_access`, no refresh grant, no `api` scope. Redirect `{simulatorOrigin}/signin-oidc`, post-logout `{simulatorOrigin}/`. Secret `Identity:SimulatorClientSecret`, origin `Identity:SimulatorOrigin`. Revocation and end session as for the other clients (R21). |
| R7 | **Allow-list.** `simulator` → `SimulatorOperator` only. `SimulatorOperator` is on no other client's row. Wrong role → no code, uniform failure (identity-auth R4, R20). |
| R8 | **Identity cookie scheme.** `identity` registers a cookie scheme for `simulator` (name example `.AspNetCore.Identity.Simulator`), with the same rules as the other three: authorize and hosted login use only this scheme for `client_id=simulator`, logout clears only it, the security-stamp check applies (identity-auth amendments 2026-10-27 and 2026-11-13). |
| R9 | **Bootstrap.** On start, `webapi` creates one `SimulatorOperator` from `Bootstrap:SimulatorOperator:Email` and `Bootstrap:SimulatorOperator:Password` (optional `Name`, default `Simulator Operator`) when none exists. Same rules as the bootstrap SystemAdmin (identity-auth amendment 2026-11-12, A25–A31): dual-write, Active, email confirmed; one key without the other, a bad email or a password that fails the Identity password options stops `webapi` before it listens; no key → a warning and no person; an existing `SimulatorOperator` (any status) is never changed. |
| R10 | **Development seed.** `DevelopmentIdentitySeeder` adds one `SimulatorOperator`, `simulator-operator@localhost`, with the same Development password as the seeded SystemAdmin. |

### 4.2 Host and surfaces (simulator)

| ID | Rule |
| --- | --- |
| R11 | **Three ports.** Outside Development the simulator listens on `8080` (control), `8081` (health) and `8082` (party). Health follows [production-hosting.md](production-hosting.md) R12–R15. In Development the AppHost names the endpoints `https` / `http` (control) and `party`. |
| R12 | **Routes by port.** `/control/*`, `/scalar`, `/openapi/*`, `/signin-oidc`, `/signout-callback-oidc`, `/signout` and `/` answer only on the control port. `/party/*` answers only on the party port. A request on the other port → 404. The check reads the connection's local port, as the health port does. The party port is `Simulator:PartyPort` (default `8082`); the control port is every other port the host listens on, except the health port outside Development, so Aspire's `https` and `http` endpoints both serve the control surface. `/health` and `/alive` stay with production-hosting R12–R13. |
| R13 | **Public host rules.** The control port is a public host: forwarded headers from the configured networks only, Data Protection keys in its own folder with an explicit application name, no in-host HTTPS redirection ([production-hosting.md](production-hosting.md) R6–R11). |
| R14 | **Own database.** The simulator reads and writes only `MyWealthSimulatorDb` (connection string `ConnectionStrings:MyWealthSimulatorDb`). It has no connection string to `MyWealthDbV2`. |
| R15 | **Own scripts.** `database/simulator/NNNN_*.sql`, numbered from `0001`, applied by the simulator at start-up with the schema library, recorded in that database's own `SchemaVersions`. The simulator is the only host that applies them. It starts listening only after the scripts are applied. The database itself is created by Aspire in Development and by hand on the VPS (§9.3). |

### 4.3 Control sign-in

| ID | Rule |
| --- | --- |
| R16 | **Session.** OIDC sign-in against `identity` with client `simulator` (R6), then an encrypted HttpOnly cookie on the simulator host. The host keeps no access or refresh token (`SaveTokens` off). The cookie lasts 8 hours from sign-in, not sliding; then the next request signs in again. Sign-out clears the cookie and ends the session on `identity`. |
| R17 | **Role check on the host too.** The host accepts a session only when its `role` claim is `SimulatorOperator`. Any other role → the cookie is not issued; the person sees a short "not allowed" page with a sign-out link. (The allow-list already stops it; this is the second line.) |
| R18 | **Pages vs. API.** `/`, `/scalar` and `/openapi/*` without a session → redirect to sign-in. `/control/*` without a session → 401, never a redirect. |
| R19 | **Cross-site writes.** Every control write is `POST` with a JSON body; a body with another content type → 415. The host sends no CORS headers. The session cookie is `SameSite=Lax`. |

### 4.4 World and clock

| ID | Rule |
| --- | --- |
| R20 | **One world.** The simulator database holds exactly one world row: `WorldId` (GUID), `Seed` (int), clock state (R21), `PartyOutage` (bool), `StartedAt` (real instant of the last reset). The first start creates it with a new `WorldId`, a random seed, offset zero, running, no outage. |
| R21 | **Simulator clock.** Simulator now = real UTC now + `ClockOffset` while running; = `PausedAt` while paused. `ClockOffset` is never negative. The simulator clock never goes back: no command moves it to an instant before the current simulator now. `ClockOffset` is whole seconds, rounded up when a command sets it, so simulator now never lands before the instant a command asked for. |
| R22 | **Advance.** `POST /control/clock/advance` with exactly one of `by` (ISO 8601 duration, `> 0`, at most `P366D`) or `to` (instant, later than simulator now, at most 366 days ahead). Running: adds to `ClockOffset`. Paused: moves `PausedAt`. Returns the world. |
| R23 | **Pause and resume.** `POST /control/clock/pause` freezes simulator now at its current value. `POST /control/clock/resume` sets `ClockOffset` so simulator now continues from the paused value without a jump. When the paused value is behind real now (the clock was paused longer than its offset), that offset would be negative, so resume sets `ClockOffset` to zero and the clock continues at real now (amended 2026-11-19 while landing, §11 point 13). Pausing a paused clock or resuming a running one returns 200 and changes nothing. |
| R24 | **Reset.** `POST /control/reset` with optional `seed` (int; omitted → random). New `WorldId`, the seed, offset zero, running, no outage, `StartedAt` = real now. Every party table (none in this slice) is emptied in the same transaction. The control log is kept. |
| R25 | **Outside ids and cursors across resets.** Every outside id and cursor the simulator ever hands out contains the `WorldId` it was made in, so an id never repeats after a reset (ADR 0021 rule 7). A cursor from an earlier world reads the current world from its start. (No id or cursor exists in this slice; the rule is fixed now so S1 does not have to.) |
| R26 | **Randomness.** Every random choice the simulator makes is drawn from a generator built from (`Seed`, purpose name, sequence number). No `Random.Shared`, no `Guid.NewGuid()` for an outside value. `WorldId` itself is a new GUID on reset. |
| R27 | **Outage.** `POST /control/party/outage` with `{ "on": true | false }`. While on, every `/party/*` route returns 503 after the credential check (R30). Control routes are not affected. |
| R28 | **Control log.** Every control write (advance, pause, resume, outage, reset) appends one row: real instant, simulator instant after the action, the operator's email from the session, the action and its body. A refused write (400) appends nothing. `GET /control/log` lists newest first, `{ items, page, pageSize, totalCount }`, page default 1, pageSize default 20, max 100. A pageSize above 100 is answered with 100, and a page or pageSize below 1 with 1; neither is a 400. |
| R29 | **Concurrency.** Control writes on the world row use its `RowVersion`. Two writes at once: one wins, the other returns 409 and changes nothing. |

### 4.5 Party surface

| ID | Rule |
| --- | --- |
| R30 | **Machine credential.** Every `/party/*` request carries header `X-Api-Key`. It is compared in constant time with `Simulator:PartyApiKey`. Missing or wrong → 401 with no body detail. Checked before the outage (R27). |
| R31 | **Key configuration.** The host refuses to start, before it listens, when `Simulator:PartyApiKey` is missing or shorter than 32 characters, in every environment. The rule reads the configuration, not the environment name. |
| R32 | **Status.** `GET /party/status` → 200 `{ "worldId", "now" }` (`now` is simulator now). During an outage → 503. This is the only party route in this slice; each party adds its own under `/party/{party}/…`. |
| R33 | **No platform knowledge.** Party responses never contain a platform PublicId, tenant code or table value that the platform did not send in the same call. (Nothing is sent in this slice.) |

---

## 5. Domain

Platform:

| Type | Kind | Change |
| --- | --- | --- |
| `UserRole` | Enum (existing) | Add `SimulatorOperator = 4`. |
| `User` | Aggregate (existing) | `User.Create` accepts `SimulatorOperator` with the SystemAdmin shape (no tenant, no adviser). There is no role-specific factory; every role goes through `User.Create` and its role-shape check. |
| `LoginEligibility` | Static rules (existing) | Platform-level roles: SystemAdmin and SimulatorOperator need no tenant (R4). |

Invariants (platform):

- A `SimulatorOperator` has no tenant and no adviser.
- A `SimulatorOperator` holds no permission and is never a `webapi` caller.

The simulator has no shared Domain project. Its own types live in `src/Simulator`:

| Type | Kind | Notes |
| --- | --- | --- |
| `World` | Entity | `WorldId`, `Seed`, `ClockOffset`, `PausedAt`, `PartyOutage`, `StartedAt`, `RowVersion`. Methods `Now(realNow)`, `Advance`, `Pause`, `Resume`, `SetOutage`, `Reset`. Holds R20–R24, R27 |
| `ControlLogEntry` | Entity | R28 |
| `SeededRandom` | Service | R26 |

Recorded in [domain-model.md](../domain-model.md) §4 and §5.

---

## 6. Database

### 6.1 Platform (`MyWealthDbV2`)

Script: `database/schema/0036_users_role_simulator_operator.sql`. Forward only.

| Table | Change | Index / FK |
| --- | --- | --- |
| `Users` | Drop and re-add `CK_Users_RoleShape`: `([Role] IN (0, 4) AND [TenantId] IS NULL AND [AdviserId] IS NULL) OR ([Role] IN (1, 2) AND [TenantId] IS NOT NULL AND [AdviserId] IS NULL) OR ([Role] = 3 AND [TenantId] IS NOT NULL AND [AdviserId] IS NOT NULL)` | none |

No data change. Recorded in [database-design.md](../database-design.md) §6.6, §9 and §15.

### 6.2 Simulator (`MyWealthSimulatorDb`)

Folder `database/simulator/`. Forward only. Never applied to `MyWealthDbV2`.

| Script | Content |
| --- | --- |
| `0001_schema_versions.sql` | `SchemaVersions`, the same shape as the platform's |
| `0002_world.sql` | `World` (one row: `Id int` PK with `CHECK (Id = 1)`, `WorldId uniqueidentifier`, `Seed int`, `ClockOffsetSeconds bigint NOT NULL CHECK (>= 0)`, `PausedAt datetimeoffset NULL`, `PartyOutage bit`, `StartedAt datetimeoffset`, `RowVersion rowversion`); `ControlLog` (`Id bigint` identity PK, `At datetimeoffset`, `SimulatorAt datetimeoffset`, `Actor nvarchar(256)`, `Action nvarchar(50)`, `Detail nvarchar(max)`, index on `At` descending) |

The world row is created by the host on first start (R20), not by the script, so its `WorldId` and seed are random.

---

## 7. Use cases

Platform:

| Kind | Name | Change |
| --- | --- | --- |
| Start-up | `SimulatorOperatorBootstrap` (new, next to `SystemAdminBootstrap`) | R9 |
| Seed | `DevelopmentIdentitySeeder` | R10 |
| Start-up | `OpenIddictSeeder` | Client `simulator` (R6) |
| Login | Hosted login, authorize, logout on `identity` | R5, R7, R8 |
| Request | Current-user load on `webapi` | R3 |

Simulator (handlers in `src/Simulator`; no MediatR required):

| Kind | Name | Returns |
| --- | --- | --- |
| Query | Get world | World item |
| Command | Advance clock, pause, resume, set outage, reset | World item |
| Query | List control log | Page of entries |
| Query | Party status | `{ worldId, now }` |

---

## 8. API

No `webapi` route changes beyond R3. `identity` gains no route (the client uses the existing `/connect/*` and `/login`).

Simulator, control port (session, R16–R19):

| Method | Route | Success | Failure |
| --- | --- | --- | --- |
| GET | `/control/world` | 200 world item | 401 |
| POST | `/control/clock/advance` | 200 world item | 400, 401, 409, 415 |
| POST | `/control/clock/pause` | 200 world item | 401, 409, 415 |
| POST | `/control/clock/resume` | 200 world item | 401, 409, 415 |
| POST | `/control/party/outage` | 200 world item | 400, 401, 409, 415 |
| POST | `/control/reset` | 200 world item | 400, 401, 409, 415 |
| GET | `/control/log` | 200 list envelope | 401 |
| GET | `/scalar`, `/openapi/v1.json` | 200 | redirect to sign-in |
| GET | `/` | redirect to `/scalar` | redirect to sign-in |

Pause and resume take an empty JSON object `{}`.

Simulator, party port (machine credential, R30):

| Method | Route | Success | Failure |
| --- | --- | --- | --- |
| GET | `/party/status` | 200 | 401, 503 |

### 8.1 Bodies

World item:

```json
{
  "worldId": "<guid>",
  "seed": 1234,
  "now": "2026-11-21T09:30:00Z",
  "paused": false,
  "partyOutage": false,
  "startedAt": "2026-11-19T08:00:00Z"
}
```

Advance (exactly one field):

```json
{ "by": "P2D" }
```

```json
{ "to": "2026-11-30T23:00:00Z" }
```

Reset: `{ "seed": 42 }` or `{}`. Outage: `{ "on": true }`.

Log entry:

```json
{
  "at": "2026-11-19T08:05:00Z",
  "simulatorAt": "2026-11-21T08:05:00Z",
  "actor": "simulator-operator@localhost",
  "action": "ClockAdvanced",
  "detail": { "by": "P2D" }
}
```

Actions: `ClockAdvanced`, `ClockPaused`, `ClockResumed`, `PartyOutageChanged`, `WorldReset`.

Party status:

```json
{ "worldId": "<guid>", "now": "2026-11-21T09:30:00Z" }
```

### 8.2 Errors

Validation 400 as ProblemDetails with `errors` (`By`, `To`, `Seed`, `On`). Both or neither of `by` / `to` → 400 `errors.By`. A `by` that is not an ISO 8601 duration → 400 `errors.By`. A `to` not after simulator now, or more than 366 days ahead → 400 `errors.To`. A body that does not bind (for example `"seed": "abc"`) → 400 from the framework. A concurrent write (R29) → 409 ProblemDetails. No problem codes.

The simulator is a third HTTP surface next to `webapi` and `identity`. [api-design.md](../api-design.md) §1 points to this section; its conventions do not otherwise apply to the simulator.

---

## 9. UI and hosting

### 9.1 UI

Scalar on the control port is the only UI in this slice. No portal, Back Office or BFF change. A Blazor page set like Back Office may replace Scalar later; that is its own amendment.

### 9.2 Development (AppHost) and tests (TestAppHost)

- AppHost: `dbserver` gains database `MyWealthSimulatorDb`. Resource `simulator` references it and `identity`, waits for both, with the `party` endpoint (R11), parameters `simulator-client-secret` and `simulator-party-api-key` (secret), and `Identity__SimulatorClientSecret` / `Identity__SimulatorOrigin` on `identity`. No platform host receives the party key in this slice.
- `Services` gains `Simulator = "simulator"`, `SimulatorDatabase = "MyWealthSimulatorDb"`, `TestSimulatorDatabase = "MyWealthSimulatorDb-test"`.
- TestAppHost: `dbserver-test` gains database `MyWealthSimulatorDb-test`, used only by `tests/Simulator.Tests`. TestAppHost does not start the simulator process; platform functional tests never need it (ADR 0021 rule 19).

### 9.3 VPS showcase

Amendment to [deployment.md](../deployment.md). Written there as §15 when accepted; folded into §3–§12 when it landed (2026-11-19):

| Item | Change |
| --- | --- |
| §3 Topology | Container `simulator`, image `mywealth-simulator`, public through Caddy at `sim.<domain>` (control port only). Party port `8082` on the internal network only, never forwarded |
| §3 Sizing | About 150–250 MB more |
| Compose profile | The service has compose profile `simulator`. It runs only when `.env` has `COMPOSE_PROFILES=simulator`. To stop it: `docker compose stop simulator`; to keep it off across deploys, remove the profile from `.env`. No other service `depends_on` it |
| §5 Keys | `simulator`: `ConnectionStrings__MyWealthSimulatorDb`, `Authentication__Authority`, `Authentication__ClientSecret`, `Authentication__PublicOrigin`, `Simulator__PartyApiKey`, `DataProtection__KeysPath`, `ForwardedHeaders__KnownNetworks__0`. `identity`: `Identity__SimulatorClientSecret`, `Identity__SimulatorOrigin`. `webapi`: `Bootstrap__SimulatorOperator__Email`, `Bootstrap__SimulatorOperator__Password` (first start only, removed after the first sign-in) |
| §6 Secrets | Fourth OIDC client secret; party API key; bootstrap simulator operator email and password |
| §7 State | `data/dataprotection/simulator/`, UID 1654 |
| §8 Database | `MyWealthSimulatorDb` created by hand once, like `MyWealthDbV2`. Start order: `sql` healthy and `identity` healthy → `simulator` |
| §9 CD | A seventh image. An unhealthy simulator fails the deploy like any other host, when its profile is on |
| §11 Security | Caddy site `sim.<domain>`: global rate limit, HSTS, forwards to `simulator:8080` only |
| §12 Runbook | Step 3: five hostnames. Step 9: also create `MyWealthSimulatorDb`. After step 11: sign in to the simulator as the bootstrap simulator operator, then remove its bootstrap keys |

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | `User` factory for `SimulatorOperator`: no tenant, no adviser. `LoginEligibility`: Active `SimulatorOperator` with no tenant → true; with a tenant → false; Disabled → false |
| Application.UnitTests | `RolePermissions.Has(SimulatorOperator, p)` is false for every policy. `ClientRoleAllowList`: `simulator` allows only `SimulatorOperator`; `SimulatorOperator` on `adviser-portal`, `customer-portal`, `back-office` → false |
| Application.FunctionalTests | A `SimulatorOperator` token → 401 on `GET /users/me`, `GET /currencies` and one ledger route. Hosted login for client `simulator`: the operator without tenantCode → code issued; SystemAdmin, TenantAdmin, Adviser, Customer → uniform failure, no code. The operator on `back-office` and `adviser-portal` → uniform failure. Identity cookie isolation: a `back-office` cookie does not authorize `simulator`, and signing out of `simulator` keeps the `back-office` cookie. A token for `simulator` has no `api` scope and no refresh token |
| Infrastructure.IntegrationTests | Script `0036`: an insert with `Role = 4`, no tenant, no adviser succeeds; `Role = 4` with a tenant violates the check; existing role shapes still pass. Bootstrap R9: the A25–A31 cases for `Bootstrap:SimulatorOperator:*`; an existing SystemAdmin does not stop the operator from being created, and the reverse; the same email as a SystemAdmin → fails on the unique index, nothing created. Development seed creates the operator once. The moved schema applicator passes its existing tests |
| Simulator.Tests | Scripts apply on an empty `MyWealthSimulatorDb-test`; a second start applies nothing. First start creates one world; a second start keeps it. Clock: advance by and to, running and paused; refuse zero, negative, both, neither, a past `to`, more than 366 days; pause → now frozen; resume → no jump; offset never negative. Reset with and without a seed: new `WorldId`, offset zero, log kept. Same seed → same draws for the same (purpose, sequence). Outage on → `/party/status` 503, control routes still 200; off → 200. Party: no key → 401, wrong key → 401, right key → 200; key checked before outage (no key during outage → 401). Start-up fails with no key and with a 31-character key. Routes by port: `/party/status` on the control port → 404, `/control/world` on the party port → 404. Control without session → 401; `/scalar` without session → redirect; a session with another role → no access. Control log: one row per write with actor and both instants; a 400 writes no row; newest first; paging. Concurrent writes → one 409 at most. Control sign-in uses a test authentication handler; the OIDC exchange itself is covered by the identity tests above |

No two-tenant isolation tests: the simulator holds no tenant data, and the platform changes add no tenant-scoped route.

---

## 11. Locked in this spec

New ADR: [adr/0021-outside-parties-behind-ports.md](../adr/0021-outside-parties-behind-ports.md) (accepted 2026-11-19). It amends [ADR 0013](../adr/0013-roles-authorization-single-user-table.md) decision 1 (five roles).

| Item | Lock |
| --- | --- |
| Host | Aspire resource `simulator`, project `src/Simulator`, image `mywealth-simulator`, hostname `sim.<domain>` |
| Database | `MyWealthSimulatorDb`, scripts in `database/simulator/` from `0001` |
| Platform script | `0036_users_role_simulator_operator.sql` |
| Role | `SimulatorOperator = 4`, platform-level, no permission, 401 on `webapi` |
| Client | `simulator`, confidential, `openid profile email`, no refresh, no `api` |
| Ports | `8080` control, `8081` health, `8082` party |
| Machine credential | `X-Api-Key` header, `Simulator:PartyApiKey`, at least 32 characters |
| Routes, policies on `webapi` | None added |

### Confirmed when this spec was accepted (2026-11-19)

Each point was confirmed as recommended and is written into the rules above as stated here. The alternatives are kept for the record.

1. **How the operator account is made** (R9, R10). Decided: bootstrap from configuration on `webapi` start, plus the Development seed. There is one operator (the owner) on the showcase. A password change is not possible from the simulator (it has no `api` scope, so it cannot call `PUT /users/me/password`); to change it, delete the operator's `Users` and `AspNetUsers` rows in SQL and let the bootstrap create it again (R9 creates one only when none exists, whatever its status). Nothing references those rows. Alternative: a `/users/simulator-operators` route under `tenants.manage` and a Back Office page. More work, and it gives SystemAdmin power over the simulator, which D16 wanted to avoid.
2. **No `api` scope and a 401 on `webapi`** (R3, R6). Decided: both. `webapi` does not check scope or audience today, so the scope alone does not keep the token out. Alternative: make `webapi` require the `api` scope for every caller, which is a wider change to every client's tokens.
3. **Machine credential** (R30, ADR 0021 rule 17). Decided: a static key the simulator owns, as a real bank would issue its own credentials. Alternative: OAuth client credentials on `identity`, which adds a grant to ADR 0014 for a test tool and makes the outside party trust the platform's identity server.
4. **A separate party port** (R11, R12). Decided: port `8082`, matched on the local port like the health port, so Caddy cannot forward party routes by mistake. Alternative: one port and a Caddy path filter, which one Caddyfile edit can undo.
5. **Running clock with offset, plus pause** (R21–R23). Decided: running by default, so outside dates follow the real days on the showcase when nobody touches it; pause gives fixed instants for a scripted demo. Alternative: a frozen clock that moves only on command, which is fully deterministic but makes every demo day need a manual advance.
6. **World id in every outside id and cursor** (R24, R25). Decided: yes, so a reset cannot make the platform's unique index (ADR 0021 rule 7) drop a new record as a duplicate of an old one. Alternative: no reset at all on production.
7. **Scenarios later** (§2 Out). Decided: the scenario library lands with S1, when the first step kinds exist. Alternative: an empty library now, which the docs rule "do not create empty files for slices not discussed" argues against.
8. **No platform reads** (§2 Out). The roadmap S0 said the simulator "may read instruments and reference prices through `webapi` read routes"; D16 gives the operator no `webapi` permission, and R3 refuses the role. Direction for S2: the order call carries the platform's reference price, and the simulator fills at it plus a seeded slippage (D11 for real instruments); simulated instruments are the simulator's own (D11). Then the simulator never reads the platform. The S2 amendment locks the S2 part.
9. **Compose profile** (§9.3). Decided: the simulator runs only when `COMPOSE_PROFILES=simulator`, so the owner can keep it off across deploys. Alternative: always on, stopped by hand, which every deploy undoes.
10. **TestAppHost** (§9.2). Decided: a database only, no process. The roadmap S0 said "an Aspire resource ... in the test AppHost"; ADR 0021 rule 19 makes the process unnecessary for tests.
11. **Schema library** (§2). Decided: move the applicator into `src/Schema` with a connection string, so the simulator does not reference `Infrastructure` (ADR 0021 rule 21). Alternative: the simulator references `Infrastructure`, which brings the platform's `DbContext` and every platform table mapping into the simulator.
12. **Session without tokens, 8 hours** (R16). Decided: the host needs no token, so it keeps none. A disabled operator keeps a session until the cookie ends. Alternative: keep refresh tokens and re-check on refresh, which needs `offline_access` for a client that calls nothing.

### Settled while landing (2026-11-19)

13. **Resume after a long pause** (R23). R21 keeps `ClockOffset` never negative and the clock never going back; R23 asked resume to continue from the paused value. When the clock is paused longer than its offset, both cannot hold. Decided while landing: the offset stays non-negative (the database checks it), and resume continues at real now in that case. A frozen past instant stays available: leave the clock paused.
14. **How `webapi` refuses the role** (R3). A JwtBearer `OnTokenValidated` hook fails a token whose `role` claim is `simulatorOperator`, so every route answers 401 through the existing challenge. `identity` sets the claim from the person row and a role never changes (R1), so the claim is the person's role, and there is no extra lookup per request. `CurrentUserAccess.Resolve` also returns no caller for the role.
15. **Routes by port** (R12). Written against the party port, so Development's two Aspire control endpoints both work; implemented as a middleware gate before authentication, so the OIDC callback paths are covered too.
16. **The dual write is one transaction** (R9). The bootstrap and the Development seed create the identity row and the person row in one database transaction, so an email already used by a SystemAdmin fails on `UQ_Users_SystemAdmin_Email` and leaves no identity row.
17. **The party port in Development** (§9.2). The AppHost gives the `party` endpoint no target-port variable and passes its target port to the host as `Simulator__PartyPort`.
18. **`identity` always needs the simulator client secret.** Like the other three, `Identity:SimulatorClientSecret` is required, so `.env` carries it even while the simulator's compose profile is off. The party key is needed only with the profile on.

Still open, not this slice:

- The first port, adapter, feed cursor table and pull job: roadmap B3 with S1
- The scenario library and its first step kinds: S1
- Business calendars and settlement dates: S2, O2
- How the simulator gets reference data for real instruments: S2 (point 8)
- A Blazor control page set
- Whether a second operator or an operator management page is ever needed

---

## 12. Suggested commits

The repository should build after each commit. Agents split each behaviour into red then green.

1. Move the schema applicator into `src/Schema` (refactor; existing applicator and script tests stay green). No behaviour change.
2. Role `SimulatorOperator` (R1, R2, R4): enum, `User` factory, `LoginEligibility`, script `0036`, `RolePermissions` + unit and integration tests.
3. `webapi` refuses the role (R3) + functional tests.
4. Bootstrap and Development seed (R9, R10) + integration tests.
5. `identity`: client `simulator`, allow-list, cookie scheme, hosted login without tenantCode (R5–R8) + functional tests.
6. Simulator host: project, `database/simulator/` scripts, applicator at start-up, ports and routes by port, health, AppHost and TestAppHost entries (R11–R15) + `Simulator.Tests`.
7. Control sign-in, role check, Scalar, content type rule (R16–R19) + tests.
8. World, clock, reset, seeded random, control log (R20–R26, R28, R29) + tests.
9. Party surface: key, status, outage (R27, R30–R33) + tests.
10. Deploy: compose service with profile, Caddy site, `.env.example`, deploy workflow image (config; exempt from test-first).
11. Repo docs: deployment, identity-auth, ADR 0013, architecture, database-design, domain-model, api-design amendments; mark this file landed; roadmap §13 step 2.

Do not put a party, a platform port, a feed table or a scenario in these commits.

---

## Amendment 2026-11-20 (S1 bank, receipts side) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [deposit-matching.md](deposit-matching.md). Rules R38–R48 and the simulator script `0003_bank.sql` live there (§4.6, §6.3); this section records what changes here.

- §2 Out "Every outside party": the bank's receipts side is in. Bank accounts (opened by `POST /control/bank/accounts` or from configuration `Simulator:Bank:StandingAccounts`, again after every reset), receipts (`POST /control/bank/receipts`, with an optional delay into the feed and an optional second delivery), its own books (`GET /control/bank/accounts`, `GET /control/bank/accounts/{accountNumber}/statement-lines`), and the party route `GET /party/bank/accounts/{accountNumber}/statement-lines?after=&limit=`. Payments, returns, recalls and balances for reconciliation come with roadmap B6 and B7.
- R24: a reset also empties the bank tables and then opens the standing accounts. R25: bank outside ids are `{WorldId:N}-L{n}` and feed cursors carry the world id.
- R28: new control log actions `BankAccountOpened` and `BankReceiptAdded`.
- §9.2: the AppHost now gives the party key to `worker` as the `simulator` bank connection's `ApiKey`, with the party endpoint as its `BaseUrl`. The Development settings list four standing accounts that match `TestSeed`'s client money accounts.
- §11 point 7 changes: no scenario library with the receipts side. One-off control calls are enough; a named scenario earns its place when it combines steps across parties, from B6 (deposit-matching §11 point 16).

---

## Amendment 2026-11-20 (S1 bank, payments side) — accepted

Status of this amendment: `accepted` (2026-11-20), together with [withdrawals.md](withdrawals.md). Rules R48–R57 and the simulator script `0004_bank_payments.sql` live there (§4.7, §6.3); this section records what changes here.

- §2 Out "Every outside party": the bank's payments side is in. Party route `POST /party/bank/accounts/{accountNumber}/payments`, idempotent on the platform's `instructionId`; control routes `POST /control/bank/payment-mode` (`Execute`, `Hold`, `Reject`), `POST /control/bank/payments/{paymentId}/execute`, `POST /control/bank/payments/{paymentId}/return` and `GET /control/bank/payments`. An executed payment is a debit line in the feed, carrying the instruction id as `endToEndId`; a return is a credit line with `returnOf`.
- R24: a reset also empties the payments and sets the payment mode to `Execute`. R25: payment ids are `{WorldId:N}-P{n}`.
- R28: new control log actions `BankPaymentModeChanged`, `BankPaymentExecuted` and `BankPaymentReturned`. A payment received on the party port is not a control action.
- §11 point 7: still no scenario library (withdrawals §11 point 19); it is proposed for B7.
