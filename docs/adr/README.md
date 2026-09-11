---
title: Architecture decision records
status: draft
language: en
created: 2026-09-11
updated: 2026-09-12
---

# Architecture decision records

Short, dated, not re-litigated in place. Copy the template to `NNNN-short-title.md`. Numbers are not reused.

Write an ADR when the choice would surprise a later reader: host, identity, schema lifecycle, keys, tenancy, money. Everyday slice contracts belong in a Feature Spec.

## Index

| ADR | Title | Status |
| --- | --- | --- |
| [0001](0001-use-dotnet-aspire-and-clean-architecture.md) | Aspire + Clean Architecture | accepted |
| [0002](0002-use-mssql-with-aspire.md) | SQL Server hosted by Aspire | accepted |
| [0003](0003-react-redux-typescript-vite-tailwind-frontend.md) | React / Redux / TypeScript / Vite / Tailwind | accepted |
| [0004](0004-money-as-decimal-with-currency.md) | Money = decimal + currency | accepted |
| [0005](0005-shared-database-tenantid-isolation.md) | Shared database + TenantId + dual check | accepted |
| [0006](0006-email-password-jwt-authentication.md) | Identity password store; issuer is 0014 | accepted |
| [0007](0007-baseentity-primary-key-int.md) | Internal int PK + external PublicId | accepted |
| [0008](0008-schema-sql-as-source-of-truth.md) | Versioned SQL is schema truth | accepted |
| [0009](0009-currencies-catalog.md) | Platform currency catalog | accepted |
| [0010](0010-instrument-catalog.md) | Instrument catalog (Phase 2 direction) | accepted |
| [0011](0011-ledger-cash-holdings-reversal.md) | Ledger direction and reversals (Phase 2) | accepted |
| [0012](0012-user-activation-invite-deferred.md) | UserStatus machine; invitation deferred | accepted |
| [0013](0013-roles-authorization-single-user-table.md) | Four roles, named policies, one table per layer | accepted |
| [0014](0014-openiddict-authorization-code-pkce.md) | OpenIddict in Aspire `identity`; authorization code + PKCE | accepted |

0010–0011 are accepted as Phase-2 *direction*. Reopen and expand them when that phase starts. Do not implement them in Phase 1.
