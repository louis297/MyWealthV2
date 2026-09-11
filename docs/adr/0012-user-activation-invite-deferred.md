---
title: "ADR 0012: User activation status; invitation deferred"
status: accepted
language: en
date: 2026-08-31
updated: 2026-09-12
related:
  - 0006-email-password-jwt-authentication.md
  - 0014-openiddict-authorization-code-pkce.md
  - ../domain-model.md
  - ../database-design.md
---

# ADR 0012: User activation status; invitation deferred

Status: accepted

The status machine is written in full in Phase 1. The invitation product is not built.

## Context

Collapsing “admin sets a password, user can log in immediately” into a boolean `IsEnabled` leaves no place for invitation later. A full invite chain is low learning value for Phase 1.

## Decision

- `UserStatus`: `PendingActivation` / `Active` / `Disabled`. Only `Active` may complete authorization-server login.
- Write the machine once:

```text
create ──with password──► Active
create ──invite seam (no password)──► PendingActivation ──set password──► Active
Active ──disable──► Disabled
Disabled ──enable──► Active
PendingActivation ──disable──► Disabled
```

- Phase 1 does **not** implement invite email, set-password links, or forgot-password. Creating a TenantAdmin / Adviser / Customer **may set a password and land in Active**.
- Table `UserTokens` exists (store hashes only; `IdentityUserId` / `TenantId` nullable, no FK). Port `IEmailSender` is a no-op. Phase 1 has no write use case.
- Creating a Customer creates Identity. There is no Customer Portal client.
- Disabling a tenant does **not** bulk-update `User.Status`. Login checks both tenant enabled and person status.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Full invitation in Phase 1 | Mail and UX dilute the platform base |
| Customer with no login principal | The person model would have to change later |
| No `UserTokens` table | Invitation would require a schema change |
| Boolean `IsEnabled` only | No landing place for the invite transition |

## Consequences

The identity slice is larger than “just login” (state machine + empty port + table) but ships no invite product. Activate / disable / password change stay on `webapi` and raise domain events that drive revocation (ADR 0014).
