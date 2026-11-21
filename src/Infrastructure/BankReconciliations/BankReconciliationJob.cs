using Hangfire;

namespace MyWealthV2.Infrastructure.BankReconciliations;

/// <summary>A daily run in which the reconciliation of at least one client money account failed (R16).</summary>
public sealed class BankReconciliationFailedException(IReadOnlyList<Guid> clientMoneyAccounts)
    : Exception($"Bank reconciliation failed for client money accounts {string.Join(", ", clientMoneyAccounts)}.");

/// <summary>One run of <c>bank-reconciliation</c> (bank-reconciliation R16, R18).</summary>
public sealed class BankReconciliationJob
{
    public const string JobId = "bank-reconciliation";

    public Task Run(CancellationToken cancellationToken) => throw new NotImplementedException();
}

/// <summary>R16: daily at 17:00 UTC on queue <c>bank</c>.</summary>
public static class BankReconciliationRecurringJob
{
    public static void Register(IRecurringJobManager recurring)
    {
    }
}
