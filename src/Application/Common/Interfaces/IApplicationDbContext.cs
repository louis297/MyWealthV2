using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }

    DbSet<Tenant> Tenants { get; }

    DbSet<Instrument> Instruments { get; }

    DbSet<Account> Accounts { get; }

    DbSet<Transaction> Transactions { get; }

    DbSet<TransactionCashLeg> TransactionCashLegs { get; }

    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);

    Task LockAccountAsync(int accountId, CancellationToken cancellationToken);

    Task LoadCashLegAsync(Transaction transaction, CancellationToken cancellationToken);
}
