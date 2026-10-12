---
title: "ADR 0017: Background worker for async tasks"
status: proposed
language: en
date: 2026-10-12
updated: 2026-10-12
related:
  - ../architecture.md
  - ../function-plan.md
  - ../features/reports.md
---

# ADR 0017: Background worker for async tasks

Status: proposed

## Context

Phase 3 opens with Reports. Reports need both manual (on-demand) and recurring automatic execution. Recurring schedules must be editable at any time (frequency, time, active flag). Later phases will also need other asynchronous work (invitation email, notifications, fee calculations, etc.).

The existing hosts (`identity`, `webapi`, `bff-adviser-portal`, `adviser-portal`) are request-driven. Long-running or scheduled work does not belong on the resource API request path.

Hangfire is a natural fit on .NET for durable, retryable, recurring, and one-off jobs with a SQL Server storage backend. The choice is whether to host it inside `webapi`, create one general-purpose worker, or create a separate worker per concern.

## Decision

Introduce one general-purpose background worker as a new Aspire resource.

- Project: `src/Worker`
- Aspire resource name: `worker`
- Hosts Hangfire Server (and, in Development, the Hangfire Dashboard).
- Hangfire storage uses SQL Server against `MyWealthDbV2`, in a dedicated `Hangfire` schema.
- Business logic stays in `Application`. The worker only resolves and executes Application services / MediatR handlers. It does not own domain rules or direct table access for business data.
- Manual triggers enter through `webapi` endpoints (named policies + handler scope checks) and enqueue a Hangfire job.
- Recurring triggers are Hangfire recurring jobs. A `ReportSchedule` (or later equivalent) record owns the scope and the schedule definition. Changing the schedule updates or replaces the Hangfire recurring job. The job carries the schedule’s scope; generation logic re-validates that scope.
- Use Hangfire queues for light isolation (`reports`, `email`, default). Do not create a separate worker project per task type yet.
- Email and other future async work land in the same worker unless a later ADR splits them.

## Alternatives considered

| Option | Why not |
| --- | --- |
| Hangfire inside `webapi` | Mixes request lifetime with long-running work. Harder to scale or restart independently. Dashboard and server lifetime couple to the API process. |
| One worker project per concern (`ReportsWorker`, `EmailWorker`, …) from day one | Extra Aspire resources, configuration, and operational surface before any concrete resource or failure-domain need exists. Queues already give a migration path. |
| Custom scheduler / hosted service without Hangfire | Re-implements persistence, retries, dashboard, and dynamic recurring jobs that Hangfire already provides. |

## Consequences

- New host in the Aspire graph. `AppHost` orchestrates `worker` alongside the existing resources. The worker receives the same database connection string.
- Schema scripts remain the source of truth for business tables. Hangfire may create its own tables on first run inside the `Hangfire` schema; document that exception, or add an explicit script if the team prefers full control.
- Authorization for scheduled work cannot rely on an HTTP user context. Scope is carried by the schedule record and re-checked inside Application services.
- Later phases inherit a single place to enqueue email, notifications, and other async work. Splitting into multiple workers remains possible by moving queues to additional processes.
- Reports Feature Spec ([features/reports.md](../features/reports.md)) is the first consumer.

## Links

- Feature specs: [features/reports.md](../features/reports.md)
- Related ADRs: [0001](0001-use-dotnet-aspire-and-clean-architecture.md), [0002](0002-use-mssql-with-aspire.md), [0008](0008-schema-sql-as-source-of-truth.md), [0013](0013-roles-authorization-single-user-table.md)
