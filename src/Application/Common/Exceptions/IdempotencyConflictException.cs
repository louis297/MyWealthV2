namespace MyWealthV2.Application.Common.Exceptions;

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException()
        : base("This Idempotency-Key was already used for a different request.")
    {
    }
}
