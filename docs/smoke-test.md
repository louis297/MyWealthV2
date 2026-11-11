---
title: Smoke test
status: draft
language: en
created: 2026-11-12
updated: 2026-11-12
related:
  - README.md
  - glossary.md
  - deployment.md
  - adviser-portal/adviser-portal-dashboard.md
  - customer-portal/customer-portal.md
  - features/tenants.md
  - features/identity-auth.md
---

# Smoke test

A small Playwright suite that answers one question: **is the system on?** It signs in to each portal through hosted login and reads one page. It is not an end-to-end suite of business rules. Those stay in the functional tests.

The same suite runs in two places:

- **Locally**, against the Aspire AppHost in Development.
- **In CD**, from GitHub Actions against the public hostnames after each deploy ([deployment.md](deployment.md) §9).

This doc is `draft`. It is not a build contract.

---

## 1. Scope

**In**

- Two read-only test cases (§2)
- The smoke tenant and the data the cases assert (§3)
- The test project, its configuration and where the passwords live (§4, §5)
- How the suite runs locally and in CD (§6, §7)

**Out**

- Any write through a portal (post, create, close)
- Back Office. CI never signs in as SystemAdmin
- Role allow-list, tenant isolation, net-worth aggregation and other rules the functional tests already prove
- Visual comparison, performance, accessibility checks
- Running on every pull request. The CI workflow does not start the hosts

---

## 2. Test cases

| # | Account | Steps | Pass when |
| --- | --- | --- | --- |
| S1 | Smoke Adviser | Open the Adviser Portal origin → redirected to hosted login → enter TenantCode `smoke`, email, password → back on `/dashboard` | The Dashboard is rendered and its net-worth section is visible |
| S2 | Smoke Customer | Open the Customer Portal origin → hosted login → open `/accounts` | The account list shows `Smoke Cash` |

- Each case starts in a fresh browser context, so no cookie is shared between S1 and S2.
- Locators use roles, labels and visible text, not CSS classes. The suite does not lock page markup.
- The suite checks that the site is on, not that numbers are right. S1 does not assert the net-worth amount; checking values is left to later tests.
- S1 proves TLS, routing, OIDC through `bff-adviser-portal`, `webapi` and SQL. S2 proves the second OIDC client and Customer scope.
- Neither case posts anything. They can run any number of times without changing data.

---

## 3. Smoke tenant

The **smoke tenant** is a tenant used only by this suite. It stays enabled.

| Item | Value |
| --- | --- |
| Tenant | Name `Smoke Test`, TenantCode `smoke`, reporting currency `NZD` |
| TenantAdmin | `smoke-admin@localhost` — needed only to create the rows below by hand in production |
| Adviser | `Smoke Adviser`, `smoke-adviser@localhost` |
| Customer | `Smoke Customer`, `smoke-customer@localhost`, assigned to Smoke Adviser |
| Account | `Smoke Cash`, type Bank, currency `NZD`, owned by Smoke Customer |
| Transaction | One `TransferIn` of `NZD 1,000.00` into Smoke Cash |

- The data is **cash only, in the reporting currency**. Net worth then needs no instrument price and no FX rate, so it always renders and stays stable for any later test that checks the amount.
- Emails and the TenantCode are the same in every environment. Only the passwords and the origins differ.
- Passwords are long random strings (at least 32 characters). They are never in the repository.

**Production.** Created by hand after the first deploy ([deployment.md](deployment.md) §12): SystemAdmin creates the tenant and its TenantAdmin in Back Office; that TenantAdmin creates the Adviser, the Customer, the Account and the `TransferIn` in the Adviser Portal. The TenantAdmin password is kept by the owner, not in GitHub.

**Development.** The Development seed creates the smoke tenant and its rows when both smoke passwords are configured (§5), and skips it with a log line when either is missing. It is idempotent on natural keys (TenantCode, email, account name). It does not reset an existing login's password; to change a local smoke password, recreate the local database. No TenantAdmin is seeded for the smoke tenant.

Seeding the smoke tenant in Development is allowed by [tenants.md](features/tenants.md) R17 (amendment 2026-11-12).

---

## 4. Test project

- Project `tests/Smoke.Tests`, using `Microsoft.Playwright.NUnit` (the repository already uses NUnit). One test class per portal.
- The project is **not** in `MyWealthV2.slnx`, so `dotnet test MyWealthV2.slnx` in CI does not try to reach live hosts. The CI workflow builds it explicitly (`dotnet build tests/Smoke.Tests`) so it does not rot, and does not run it.
- Browsers are installed with the Playwright script that the build writes to the output folder (`playwright.ps1 install --with-deps chromium`). Chromium only.
- On failure the suite keeps a Playwright trace and a screenshot per case under `TestResults/`. CD uploads them as a workflow artifact.
- Time limits: 30 seconds per navigation, 2 minutes per case. A cold start after deploy is covered by the health wait in [deployment.md](deployment.md) §9, not by long test timeouts.

---

## 5. Configuration and secrets

The suite reads the `Smoke` configuration section. Environment variables override user secrets.

| Key | Secret | Local source | CD source |
| --- | --- | --- | --- |
| `Smoke:AdviserPortalUrl` | No | Default: the local `bff-adviser-portal` origin | Workflow env, `https://adviser.<domain>` |
| `Smoke:CustomerPortalUrl` | No | Default: the local `bff-customer-portal` origin | Workflow env, `https://customer.<domain>` |
| `Smoke:AdviserPassword` | Yes | User secrets | GitHub environment secret `SMOKE_ADVISER_PASSWORD` → env `Smoke__AdviserPassword` |
| `Smoke:CustomerPassword` | Yes | User secrets | GitHub environment secret `SMOKE_CUSTOMER_PASSWORD` → env `Smoke__CustomerPassword` |

TenantCode and emails are constants in the test project (§3).

**One local secret store.** `tests/Smoke.Tests` uses the same `UserSecretsId` as `src/AppHost`. The AppHost reads the two passwords from the same keys and passes them to the host that runs the Development seed. One `dotnet user-secrets set` therefore feeds both the seed and the tests:

```bash
dotnet user-secrets set "Smoke:AdviserPassword" "<long random string>" --project src/AppHost
dotnet user-secrets set "Smoke:CustomerPassword" "<long random string>" --project src/AppHost
```

**GitHub.** Both secrets live in the GitHub environment `production`, next to the SSH key ([deployment.md](deployment.md) §6). The production values are different from any local value. The test job is the only job that reads them.

If a password is missing, the suite fails at start with a message naming the missing key. It does not skip.

---

## 6. Running locally

1. Set the two user secrets (§5).
2. Start the AppHost. The Development seed creates the smoke tenant on first start.
3. Build the test project and install Chromium (§4).
4. `dotnet test tests/Smoke.Tests`.

---

## 7. Running in CD

- Workflow `.github/workflows/smoke.yml`, triggered by `workflow_call` (the deploy workflow, [deployment.md](deployment.md) §9 step 4) and `workflow_dispatch` (by hand, runbook §12 step 9).
- Job environment `production`. Steps: checkout → set up .NET → build `tests/Smoke.Tests` → install Chromium → `dotnet test tests/Smoke.Tests` → upload `TestResults/` when the job fails.
- A failed run fails the deploy, and the deploy rolls back the image tag ([deployment.md](deployment.md) §9 step 5).

---

## 8. Code changes this doc needs

None of these exist yet.

| # | Change | Kind |
| --- | --- | --- |
| 1 | `tests/Smoke.Tests` with S1 and S2 | Test code |
| 2 | Development seed for the smoke tenant (§3) | Seed, Development only |
| 3 | AppHost reads `Smoke:AdviserPassword` / `Smoke:CustomerPassword` and passes them to the seeding host | Wiring |
| 4 | CI builds `tests/Smoke.Tests` without running it | CI |
| 5 | `.github/workflows/smoke.yml` (§7) | CI |
