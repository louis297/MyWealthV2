---
title: "ADR 0018: Transactional outbox for domain events"
status: accepted
language: en
date: 2026-11-01
updated: 2026-11-01
related:
  - ../architecture.md
  - ../domain-model.md
  - ../fixes/domain-event-dispatch.md
  - 0017-background-worker.md
  - 0005-shared-database-tenantid-isolation.md
  - 0008-schema-sql-as-source-of-truth.md
---

# ADR 0018: Transactional outbox for domain events

Status: accepted

## Context

Domain events are raised on aggregates and dispatched via MediatR after `base.SaveChangesAsync` returns. They are **not** part of the database transaction. A handler failure leaves the domain change committed and the side effect incomplete. Inside an outer transaction the dispatch still runs before commit. The 2026-10-21 fix documented this behaviour and left a full transactional outbox out of scope. See [fixes/domain-event-dispatch.md](../fixes/domain-event-dispatch.md).

Current consumers include token revocation (UserDisabled, UserPasswordChanged, TenantDisabled), audit, and the Reports event-enqueue hook (concrete events not yet locked). Later phases need reliable async work (email, notifications, fee calculations). A message broker is not required for the current single-database Aspire hosts.

The project already has a Hangfire worker (ADR 0017) and MediatR. The gap is durable, transactional capture of domain events so side effects can be retried after commit.

## Decision

Introduce a transactional outbox for domain events. Domain changes and their events commit together. A background process in the existing worker delivers the events after commit.

1. Domain events are persisted to an `OutboxMessages` table in the **same database transaction** as the business changes that raised them. Immediate post-`SaveChanges` MediatR dispatch is removed.
2. The table is created by a versioned SQL script (ADR 0008). Hangfire storage stays in the `Hangfire` schema; the outbox is a business table in the application schema.
3. Delivery is at-least-once. Handlers must be idempotent. Token revocation and audit are the first handlers that must tolerate re-delivery.
4. Processing runs in the existing `worker` (ADR 0017). The worker claims rows, deserialises the event, and publishes via MediatR in a new scope. Success sets `Status = Processed` and `ProcessedAt`. Failure records `Error`, increments `RetryCount`, and sets `Status = Failed` (retryable) or `DeadLettered` (limit reached). Claim uses `Status` plus `RowVersion`.
5. First slice covers **domain events only**. Integration events and an external message bus are later work. Existing MediatR handlers continue to receive the same events; only the timing and reliability of delivery change.
6. An outbox row carries `TenantId` as a column when the event is tenant-scoped (ADR 0005). Platform or cross-tenant events leave it null. The column exists so the worker can set tenant context and queries can filter without parsing JSON. The same value may also appear inside `Payload`.
7. `Payload` is the serialised domain event. Its shape is defined by the event type the aggregate raised. The outbox does not invent extra fields inside the JSON. `Type` is the event type name used to deserialise.
8. The Reports event-enqueue hook becomes a MediatR handler that reacts to selected domain events and enqueues a Hangfire job. No second event pipeline is introduced for reports.
9. Outbox processing is not performed on the `webapi` request path.
10. First slice does **not** store a Hangfire `JobId`, a target, a `CorrelationId`, or `AggregateType` / `AggregateId`. Business identity of the event is `Type` + `Payload` (the payload carries the aggregate id and the facts). A `CorrelationId` would not link to a request unless a separate log or command record also stored it; that mechanism does not exist and is not required for this slice. Those columns may be added later by forward ALTER if a concrete need appears.

`Status` values (`nvarchar`, check constraint, same style as `ReportExecutions.Status`):

| Status | Meaning |
| --- | --- |
| `Pending` | Available to claim |
| `Processing` | Claimed by a job, in progress |
| `Processed` | Delivered successfully |
| `Failed` | Retryable (`RetryCount` below the limit) |
| `DeadLettered` | Retry limit reached; no further automatic delivery |

Minimum columns (exact script in the implementing slice):

| Column | Notes |
| --- | --- |
| `Id` | uniqueidentifier. Row identity |
| `Type` | Event type name. Routing and deserialisation |
| `Payload` | JSON. Serialised domain event. Shape owned by the event type |
| `OccurredAt` | datetimeoffset |
| `TenantId` | nullable. Infrastructure column for context and queries. May also appear in `Payload` |
| `Status` | `Pending` / `Processing` / `Processed` / `Failed` / `DeadLettered` |
| `ProcessedAt` | nullable. Set when `Status` becomes `Processed` |
| `Error` | nullable |
| `RetryCount` | int |
| audit columns + `RowVersion` | match existing tables. `RowVersion` used when claiming |

## Alternatives considered

| Option | Why not |
| --- | --- |
| Keep post-SaveChanges MediatR dispatch and add retries in the handler | Still no transactional guarantee. A crash between commit and dispatch loses the event. Outer-transaction timing remains wrong. |
| Hangfire job enqueued from the request handler before commit | Job may run before the business transaction commits, or be lost if the process dies after enqueue and before commit. |
| Full message broker (RabbitMQ, Azure Service Bus, MassTransit, Wolverine) from day one | Extra infrastructure before the single-database hosts need distributed events. Outbox + Hangfire already covers the reliability gap. |
| Outbox processing inside `webapi` | Mixes request lifetime with background work. Conflicts with ADR 0017. |
| Separate outbox table per aggregate or feature | Duplicates claiming, retry, and dead-letter logic. One table is enough for the first slice. |
| Store Hangfire `JobId` or a target on the row | Delivery mechanism, not business identity. A retried row can have many jobs. `Type` + `Payload` identify the event. |
| `CorrelationId` column in the first slice | A GUID links to a request only if logs or a command table also store it. Neither exists. Not required for delivery or for knowing which domain event occurred. |
| `AggregateType` / `AggregateId` columns in the first slice | Aggregate identity already lives in the event payload. Add later only if cross-event queries need them without parsing JSON. |

## Consequences

- Domain event side effects become eventually consistent and retryable. A failed token revocation no longer silently loses the event; the command still returns success after the business transaction commits.
- Schema script and `ApplicationDbContext.SaveChangesAsync` (or a registered interceptor) change. Immediate dispatch code is removed.
- Existing MediatR handlers must be reviewed for idempotency. Token revocation is the first candidate.
- The worker gains an outbox claim-and-publish job (recurring or triggered). Hangfire dashboard shows the work.
- Reports event triggers have a reliable source. Concrete event bindings remain a Reports decision, not an outbox decision.
- Later integration events or a message broker can be added by having the outbox processor publish to the broker instead of (or in addition to) MediatR. Domain aggregates do not change.
- No change to OpenIddict token storage, identity host, or BFF.

## Links

- Feature specs: [features/outbox.md](../features/outbox.md) (accepted), [features/reports.md](../features/reports.md) (event enqueue hook)
- Fix: [fixes/domain-event-dispatch.md](../fixes/domain-event-dispatch.md)
- Related ADRs: [0005](0005-shared-database-tenantid-isolation.md), [0008](0008-schema-sql-as-source-of-truth.md), [0017](0017-background-worker.md)
