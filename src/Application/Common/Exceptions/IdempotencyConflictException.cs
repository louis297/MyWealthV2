namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>The Idempotency-Key was used before with a different request (api-design §4.2 <c>idempotency_key_reused</c>).</summary>
public sealed class IdempotencyConflictException()
    : ProblemException(
        ProblemKind.Conflict,
        "idempotency_key_reused",
        "Conflict",
        "This Idempotency-Key was already used for a different request.");
