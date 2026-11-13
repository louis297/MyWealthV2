---
title: "Known issue — refresh grant does not re-check person or tenant status"
status: open
language: en
created: 2026-11-13
updated: 2026-11-13
related:
  - ../features/identity-auth.md
  - ../features/outbox.md
  - ../deployment.md
---

# Known issue: refresh grant does not re-check person or tenant status

**Status: open.** Accepted for the showcase. Check this first when a disabled person, a person of a disabled tenant, or a person who changed their password still gets new tokens.

## Problem

The token endpoint does not re-check `Users.Status` or the tenant flag on a refresh grant. Disable and password change revoke the subject's tokens through the outbox: `UserDisabled`, `TenantDisabled` and `UserPasswordChanged` are written to `OutboxMessages`, and the `worker` runs the revocation handlers after commit ([identity-auth.md](../features/identity-auth.md), amendment 2026-11-11, revocation is delivered by the outbox).

Until the event is delivered, an existing refresh token still works. The window is normally the outbox poll interval plus retries. After five failed attempts the row becomes `DeadLettered` ([outbox.md](../features/outbox.md)), and the refresh token stays valid until its 14-day absolute lifetime.

What limits the damage:

- `webapi` re-checks person and tenant status on every request, so the access tokens minted by that refresh get 403 on every `webapi` route ([identity-auth.md](../features/identity-auth.md), amendment 2026-11-13, `webapi` re-checks person and tenant status).
- New hosted login and `/connect/authorize` check `LoginEligibility` on every request.
- A password change also ends silent re-authorization through the identity cookie (amendment 2026-11-13, identity cookie checks the security stamp). The refresh token itself still waits for the outbox.

What stays open: a dead-lettered `UserPasswordChanged` leaves an old refresh token able to mint access tokens that `webapi` accepts, because the person is still `Active`.

## Symptoms

| Symptom | Cause |
| --- | --- |
| A disabled person's portal session stays signed in but every page fails with 403 | The refresh token was not revoked yet, or its revocation event is `DeadLettered` |
| A person changed their password, and another device keeps working | `UserPasswordChanged` was not delivered yet, or is `DeadLettered` |

## How to check

On the VPS, in the folder that holds `compose.yml` and `.env`:

```bash
SA=$(grep '^SQL_SA_PASSWORD=' .env | cut -d= -f2-)
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA" -C -d MyWealthDbV2 -Q "
SELECT Id, Type, Status, RetryCount, OccurredAt, Error
FROM OutboxMessages
WHERE Status IN (N'Failed', N'DeadLettered')
ORDER BY OccurredAt DESC"
```

Then read the `worker` logs for the error: `docker compose logs worker`.

## How to fix

Fix the cause in the `Error` column first. Then put the row back in the queue (same folder and `SA` as above). The revocation handlers are idempotent, so a second delivery is safe.

```bash
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA" -C -d MyWealthDbV2 -Q "
UPDATE OutboxMessages SET Status = N'Pending', RetryCount = 0, Error = NULL
WHERE Id = '<id from the query above>'"
```

The `worker` picks it up on its next poll.

## What we are not doing now

- Re-checking status on the refresh grant in the token endpoint. It needs its own amendment to [identity-auth.md](../features/identity-auth.md).
- Alerting on `DeadLettered` rows. This deploy has no monitoring beyond container logs ([deployment.md](../deployment.md) §2).

## Revisit when

- Real clients use the portals.
- A `DeadLettered` revocation event is found.
