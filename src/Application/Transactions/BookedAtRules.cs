namespace MyWealthV2.Application.Transactions;

/// <summary>fixes/ledger-write-guards.md Decision 6: a caller's bookedAt is not in the future.</summary>
public static class BookedAtRules
{
    /// <summary>Allows for a browser clock that runs ahead of the server.</summary>
    public static readonly TimeSpan FutureAllowance = TimeSpan.FromMinutes(5);

    public const string FutureMessage = "bookedAt cannot be in the future.";

    public static IRuleBuilderOptions<T, DateTimeOffset?> NotInTheFuture<T>(
        this IRuleBuilder<T, DateTimeOffset?> rule,
        TimeProvider time) =>
        rule.Must(bookedAt => bookedAt is null || bookedAt.Value <= time.GetUtcNow() + FutureAllowance)
            .WithMessage(FutureMessage);
}
