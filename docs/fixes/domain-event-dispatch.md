---
title: Fix — Domain event dispatch timing and reliability
status: accepted
language: en
created: 2026-10-21
updated: 2026-10-21
related:
  - ../architecture.md
  - ../domain-model.md
---

# Fix: Domain event dispatch timing and reliability

Status: accepted

## Problem

`docs/architecture.md` lists `DispatchDomainEventsInterceptor` among the save interceptors.

The class inherits `SaveChangesInterceptor` but is not used as an interceptor. It only exposes a static method `PublishAfterCommitAsync`. `ApplicationDbContext.SaveChangesAsync` calls it manually after `base.SaveChangesAsync`:

```csharp
var result = await base.SaveChangesAsync(cancellationToken);
await DispatchDomainEvents(cancellationToken);
```

`DispatchDomainEvents` resolves `IMediator` via `IServiceProvider.GetService` and returns silently if the mediator is null.

Consequences:

1. Events run after the `SaveChanges` transaction has committed. A failure in a handler (for example, revoking tokens after a user is disabled) leaves the domain change committed and the side effect incomplete. There is no outbox or retry.
2. When `ExecuteInTransactionAsync` wraps an action that itself calls `SaveChanges`, the events fire before the outer transaction commits. The method name `PublishAfterCommitAsync` is incorrect on that path.
3. Silent skip when `IMediator` cannot be resolved hides configuration errors.
4. Handlers that open a new scope to obtain services are using a service-locator style; this is workable but should be explicit.

## Decision

Clarify and tighten the current design without introducing an outbox in this fix.

- Document that domain events are dispatched after a successful `SaveChanges` on the same context, and that they are **not** part of the database transaction.
- Keep the explicit call after `base.SaveChangesAsync` for now. Rename the method so it does not claim "AfterCommit" when called inside `ExecuteInTransactionAsync`. Document the exact timing.
- Make the failure mode observable: if `IMediator` cannot be resolved, log an error. Do not silently return. In Development, consider throwing.
- A failed event handler after commit does not roll back the original command. The command returns success; the side effect is eventually consistent. Compensating action or retry is the responsibility of the handler or a later outbox slice.
- Token revocation (and any similar security-sensitive handler) is noted as eventually consistent under this design.
- Update `architecture.md` so it no longer claims an interceptor that is not registered.

## Out of scope

- Full transactional outbox / integration-event infrastructure.
- Changing the set of domain events that are raised.
- Moving event handlers out of Application.
- Switching to a real `SaveChangesInterceptor` override in this fix.

## Impact

- `ApplicationDbContext.SaveChangesAsync` and the domain-event dispatch helper.
- Logging / error behaviour when mediator resolution fails.
- Documentation in `architecture.md` and the domain-event section of `domain-model.md`.
- No schema change.
- Existing event handlers continue to run; only the dispatch path and failure visibility change.
