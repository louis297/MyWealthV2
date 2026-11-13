---
title: "Known issue — hosted login lockout can be triggered by anyone"
status: open
language: en
created: 2026-11-13
updated: 2026-11-13
related:
  - ../features/identity-auth.md
  - ../deployment.md
  - ../smoke-test.md
---

# Known issue: hosted login lockout can be triggered by anyone

**Status: open.** Accepted for the showcase. Check this first when a person cannot sign in with the right password, or when the post-deploy smoke test fails at hosted login.

## Problem

Hosted login locks an identity user for five minutes after five wrong passwords in a row ([identity-auth.md](../features/identity-auth.md), amendment 2026-11-13, lockout). Anyone who knows a tenantCode and an email can send those five wrong passwords and keep that account locked, from any IP. The person only sees the uniform `Invalid login.` (R3); the page does not say the account is locked.

The smoke tenant's TenantCode and emails are in [smoke-test.md](../smoke-test.md) §3, so the smoke accounts are the easiest target. A locked smoke Adviser or Customer fails the post-deploy smoke test, and the deploy rolls back.

Two small timing differences remain. A wrong password for an existing identity user writes `AccessFailedCount`, and an unknown tenantCode skips one lookup. Both differ by a few milliseconds; the password hash that every attempt pays for is far larger.

## Symptoms

| Symptom | Cause |
| --- | --- |
| A person gets `Invalid login.` with the right password and recovers after about five minutes | Five wrong passwords in a row, by the person or by someone else |
| Post-deploy smoke test fails at hosted login, then the deploy rolls back | A smoke account is locked |

## How to check

On the VPS, in the folder that holds `compose.yml` and `.env`:

```bash
SA=$(grep '^SQL_SA_PASSWORD=' .env | cut -d= -f2-)
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA" -C -d MyWealthDbV2 -Q "
SELECT u.Email, t.Code, a.AccessFailedCount, a.LockoutEnd
FROM AspNetUsers a
JOIN Users u ON u.IdentityUserId = a.Id
LEFT JOIN Tenants t ON t.Id = u.TenantId
WHERE a.LockoutEnd > SYSDATETIMEOFFSET() OR a.AccessFailedCount > 0"
```

## How to fix

Wait five minutes, or clear the lockout for one person (same folder and `SA` as above). For a SystemAdmin, drop the `Tenants` join and use `u.TenantId IS NULL`.

```bash
docker compose exec sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA" -C -d MyWealthDbV2 -Q "
UPDATE a SET a.LockoutEnd = NULL, a.AccessFailedCount = 0
FROM AspNetUsers a
JOIN Users u ON u.IdentityUserId = a.Id
JOIN Tenants t ON t.Id = u.TenantId
WHERE u.Email = N'smoke-adviser@localhost' AND t.Code = N'smoke'"
```

Then re-run the deploy workflow if the smoke test failed.

## What we are not doing now

- An unlock action in Back Office. It needs its own amendment and a route.
- Telling the person the account is locked. It would let anyone list existing accounts (R3).
- Exempting the smoke accounts from lockout. It would leave one known account open to unlimited guessing.

## Revisit when

- Real clients use the portals, or a lockout is reported that the person did not cause.
- The smoke test fails at hosted login more than once.
