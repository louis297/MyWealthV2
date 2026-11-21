using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>Runs one bank reconciliation of a client money account (bank-reconciliation R6–R15).</summary>
public interface IReconcileClientMoneyAccount
{
    /// <summary>Reads the inputs at SNAPSHOT, computes the run and stores it; answers the run's PublicId.</summary>
    Task<Guid> RunAsync(
        int clientMoneyAccountId,
        ReconciliationTrigger trigger,
        int? ranByUserId,
        CancellationToken cancellationToken);
}

public sealed class ReconcileClientMoneyAccount : IReconcileClientMoneyAccount
{
    public Task<Guid> RunAsync(
        int clientMoneyAccountId,
        ReconciliationTrigger trigger,
        int? ranByUserId,
        CancellationToken cancellationToken) => throw new NotImplementedException();
}
