---
title: "ADR 0001: Use .NET Aspire and Clean Architecture"
status: accepted
language: en
date: 2026-08-18
updated: 2026-09-12
related:
  - ../architecture.md
  - ../function-plan.md
---

# ADR 0001: Use .NET Aspire and Clean Architecture

Status: accepted

## Context

MyWealthV2 is a learning wealth-management SaaS. It needs orchestrated infrastructure, a clear backend boundary, and a separate frontend.

## Decision

- Use .NET Aspire as the application host and infrastructure orchestrator (`src/AppHost`).
- Backend follows Clean Architecture: Domain / Application / Infrastructure. Composition roots are split by process: `identity` (`src/IdentityHost`) and `webapi` (`src/Web`).
- The frontend is an independent React + Redux + TypeScript app. The Phase-1 resource name is `adviser-portal`.
- Phase-1 Aspire graph: `dbserver` → `MyWealthDbV2`, plus `identity`, `webapi`, and `adviser-portal`.
- Two ASP.NET projects mean two Kestrel processes. Aspire does not merge them onto one process.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Minimal APIs plus a simple three-layer backend | Not enough learning value |
| Hand-written Docker Compose | Loses Aspire local UX and typed configuration |
| OpenIddict inside `webapi` | Rejected by ADR 0014 |

## Consequences

SQL container, connection strings, health checks, and service discovery stay unified. Layering is mandatory: Domain has no project references; entities are hand-written.
