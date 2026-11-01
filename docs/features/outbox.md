---
title: "Outbox"
status: accepted
phase: 3
language: en
owner: ""
created: 2026-11-01
last_updated: 2026-11-01
related:
  - ../function-plan.md
  - ../domain-model.md
  - ../database-design.md
  - ../architecture.md
  - ../glossary.md
  - ../adr/0018-transactional-outbox.md
  - ../adr/0017-background-worker.md
  - ../fixes/domain-event-dispatch.md
  - reports.md
---

# Outbox

Cross-cutting delivery for domain events. Aggregates already raise events. This slice replaces immediate post-`SaveChanges` MediatR dispatch with a transactional outbox and worker delivery. Decision: [ADR 0018](../adr/0018-transactional-outbox.md) (accepted). Adds script `0020_outbox.sql`. No new HTTP policy. No portal page.

**Status is `accepted`.** Implementation follows this file.

---

## 1. Summary

Domain changes and the domain events they raise commit together in `OutboxMessages`. The existing `worker` claims rows and publishes them through MediatR after commit. Delivery is at-least-once. Handlers stay in Application. Immediate post-`SaveChanges` dispatch is removed.

---

## 2. Scope

**In**

- Table `OutboxMessages` (application schema, not `Hangfire`).
- Write path: collect domain events during save and insert outbox rows in the same database transaction. Clear events on the aggregates after they are captured.
- Remove immediate post-`SaveChanges` MediatR dispatch.
- Worker claim-and-publish job. New scope. MediatR `Publish`. Update `Status`.
- `Status` machine: `Pending` / `Processing` / `Processed` / `Failed` / `DeadLettered`.
- `TenantId` column for worker context. `Payload` is the serialised domain event.
- Idempotency review of existing token-revocation handlers (revoke of an already-revoked subject succeeds).
- Tests for same-transaction write, no immediate dispatch, retry, dead-letter, tenant context.

**Out**

- Integration events and an external message broker.
- Hangfire `JobId`, target, `CorrelationId`, `AggregateType`, `AggregateId` columns.
- New domain events. Concrete Reports event bindings.
- HTTP API for the outbox. Portal UI. Manual replay endpoint.
- Inbox pattern.

---

## 3. Stories

1. As the platform, when a command commits a domain change, I persist every raised domain event in the same transaction so a crash cannot lose the event.
2. As the worker, I claim pending or retryable rows and publish them so side effects run after commit and can be retried.
3. As an operator, I can see a row move to `Processed`, `Failed`, or `DeadLettered` so a failed token revocation is visible and not silent.

---

## 4. Rules

| ID | Rule |
| --- | --- |
| R1 | Outbox rows are inserted in the same database transaction as the business changes that raised the events. |
| R2 | Immediate post-`SaveChanges` MediatR dispatch is removed. Handlers run only from the worker. |
| R3 | Delivery is at-least-once. Handlers must be idempotent. |
| R4 | A handler failure does not roll back the original command. The command returns success after the business transaction commits. |
| R5 | Claim uses `Status` and `RowVersion`. Only `Pending`, or `Failed` under the retry limit, may be claimed. |
| R6 | `Payload` is the serialised domain event. The outbox does not add fields inside the JSON. |
| R7 | `TenantId` is a column. Platform or cross-tenant events store null. |
| R8 | Processing is not on the `webapi` request path. |
| R9 | First slice stores no `JobId`, target, `CorrelationId`, or aggregate id columns. |
| R10 | Reports event enqueue stays a MediatR handler. This slice does not bind concrete report events. |

---

## 5. Domain

No new aggregate. Existing domain events are unchanged.

| Type | Kind | Notes |
| --- | --- | --- |
| Domain events (existing) | event | Still raised by aggregates. Captured into the outbox instead of dispatched inline. |

Invariants:

- An outbox row is infrastructure. It is not a domain entity and is not raised as an event.
- Economic and people aggregates do not know about the outbox.

Domain events:

- Unchanged set. See [domain-model.md](../domain-model.md) §6.

---

## 6. Database

| Table | Change | Index / FK |
| --- | --- | --- |
| `OutboxMessages` | add | See open items for index. No FK to business tables. |

Script: `database/schema/0020_outbox.sql`.

Columns:

| Column | Notes |
| --- | --- |
| `Id` | uniqueidentifier |
| `Type` | `nvarchar(200)` not null. Event class full name (`Namespace.EventName`). Not assembly-qualified |
| `Payload` | `nvarchar(max)` not null. JSON. Serialised domain event. System.Text.Json defaults used elsewhere in the application |
| `OccurredAt` | datetimeoffset. Event timestamp if the event has one; otherwise `DateTimeOffset.UtcNow` at capture |
| `TenantId` | nullable. From the event’s `TenantId` property if present; otherwise `ITenantIdAccessor` when engaged; otherwise null |
| `Status` | `nvarchar(20)` not null. Check `Pending` \| `Processing` \| `Processed` \| `Failed` \| `DeadLettered` |
| `ProcessedAt` | nullable. Set when `Status` becomes `Processed` |
| `Error` | `nvarchar(2000)` null |
| `RetryCount` | int. Number of failed publish attempts |
| audit columns + `RowVersion` | Same audit columns as other business tables. `RowVersion` used when claiming. `CreatedBy` / `ModifiedBy` from the current user when present; null on the worker’s status updates is allowed |

Index: `(Status, OccurredAt)`. No FK to business tables.

Update [database-design.md](../database-design.md) in the same change. Do not add EF migrations.

---

## 7. Application use cases

No public command or query. Internal only.

| Kind | Name | Returns | Checks |
| --- | --- | --- | --- |
| Internal | CaptureOutboxOnSave | — | Same transaction as the business save. Clear aggregate events after capture. |
| Internal | ProcessOutbox | — | Claim by Status + RowVersion. New scope. Set tenant context from the row. MediatR Publish. Update Status. |

---

## 8. API

None. No route, no policy, no BFF allow-list change.

---

## 9. UI

None.

---

## 10. Tests

| Project | Assert |
| --- | --- |
| Domain.UnitTests | None required. Aggregates still raise the same events. |
| Application.FunctionalTests | A command that raises an event commits the business row and an `OutboxMessages` row together. No handler runs on the request path. |
| Infrastructure.IntegrationTests | Worker processes a `Pending` row to `Processed`. A failing handler moves the row to `Failed` and then `DeadLettered` at 5. Tenant-scoped row sets tenant context before publish. A `Processing` row older than 5 minutes is claimable again. |

---

## 11. Locked in this spec

From ADR 0018, plus the former open items. Not re-opened here.

| Item | Lock |
| --- | --- |
| Decision | [ADR 0018](../adr/0018-transactional-outbox.md) accepted |
| Write | Same transaction as the business change |
| Deliver | Existing `worker`, MediatR, after commit |
| Semantics | At-least-once; handlers idempotent |
| Status | `Pending` / `Processing` / `Processed` / `Failed` / `DeadLettered` |
| Payload | Serialised domain event only |
| TenantId | Column; null for platform / cross-tenant |
| Not stored | JobId, target, CorrelationId, AggregateType, AggregateId |
| Scope | Domain events only. No broker. No HTTP |
| Script | `database/schema/0020_outbox.sql` |
| Lengths / index | `Type nvarchar(200)`, `Payload nvarchar(max)`, `Error nvarchar(2000)`, `Status nvarchar(20)` with the check above. Index `(Status, OccurredAt)`. No FK |
| Type format | Event class full name. System.Text.Json. Not assembly-qualified |
| Stuck Processing | A row in `Processing` for more than 5 minutes is claimable again. New claim sets `Processing` and increments `RetryCount` |
| Retry | 5 failed publish attempts, then `DeadLettered`. No per-row delay column. Next poll is the backoff |
| Worker | Hangfire recurring job, every 10 seconds, queue `outbox`. Up to 20 rows per run, oldest `OccurredAt` first. No post-commit enqueue |
| Claim | One worker process. `UPDATE … OUTPUT` (or equivalent) filtered by `Status` and `RowVersion` |
| TenantId on write | Event `TenantId` if present; else `ITenantIdAccessor` when engaged; else null |
| Write hook | Explicit call from `ApplicationDbContext.SaveChangesAsync`. Outbox entities added before `base.SaveChangesAsync`. No new `SaveChangesInterceptor` |
| OccurredAt | Event timestamp if present; else `DateTimeOffset.UtcNow` at capture |
| Audit | Same columns as other business tables. Worker status updates may leave `ModifiedBy` null |
| Token revocation | In scope. Repeat revoke of an already-revoked subject succeeds |

Still open, not this slice:

- Integration events and a message broker.
- Manual replay API.
- `CorrelationId` once a request log exists.
- Concrete Reports event bindings.

---

## 12. Suggested commits

The repository should build after each commit. Agents split each line into red then green.

1. Script `0020_outbox.sql` and EF mapping for `OutboxMessages`. No behaviour change.
2. Capture domain events into the outbox in the same transaction. Remove immediate MediatR dispatch. Functional test: business row and outbox row commit together; handler does not run on the request.
3. Worker recurring job: claim, publish, status transitions, stuck-`Processing` recovery, dead-letter at the limit. Integration tests for Processed / Failed / DeadLettered.
4. Make token-revocation handlers idempotent. Test repeat delivery.
