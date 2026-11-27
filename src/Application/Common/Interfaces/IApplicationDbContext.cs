using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }

    DbSet<Tenant> Tenants { get; }

    DbSet<ClientMoneyAccount> ClientMoneyAccounts { get; }

    DbSet<BankFeedCursor> BankFeedCursors { get; }

    DbSet<MarketDataFeed> MarketDataFeeds { get; }

    DbSet<BankStatementLine> BankStatementLines { get; }

    DbSet<BankReconciliation> BankReconciliations { get; }

    DbSet<BankReconciliationItem> BankReconciliationItems { get; }

    DbSet<NominatedBankAccount> NominatedBankAccounts { get; }

    DbSet<Withdrawal> Withdrawals { get; }

    DbSet<OutgoingPayment> OutgoingPayments { get; }

    DbSet<AccountClosure> AccountClosures { get; }

    DbSet<CustomerTaxProfile> CustomerTaxProfiles { get; }

    DbSet<InterestRateSchedule> InterestRateSchedules { get; }

    DbSet<InterestRateBand> InterestRateBands { get; }

    DbSet<InterestAccrual> InterestAccruals { get; }

    DbSet<InterestRun> InterestRuns { get; }

    DbSet<ValuationRun> ValuationRuns { get; }

    DbSet<MyWealthV2.Domain.Valuations.StoredValuation> StoredValuations { get; }

    DbSet<CorporateAction> CorporateActions { get; }

    DbSet<CorporateActionEntitlement> CorporateActionEntitlements { get; }

    DbSet<CorporateActionAnnouncement> CorporateActionAnnouncements { get; }

    DbSet<Order> Orders { get; }

    DbSet<OrderFill> OrderFills { get; }

    DbSet<ExecutionReport> ExecutionReports { get; }

    DbSet<BrokerFeed> BrokerFeeds { get; }

    DbSet<SafekeepingAccount> SafekeepingAccounts { get; }

    DbSet<CustodianFeedCursor> CustodianFeedCursors { get; }

    DbSet<CustodianPosition> CustodianPositions { get; }

    DbSet<CustodyMovement> CustodyMovements { get; }

    DbSet<InKindTransfer> InKindTransfers { get; }

    DbSet<AdjustmentRequest> AdjustmentRequests { get; }

    DbSet<CustodianReconciliation> CustodianReconciliations { get; }

    DbSet<TradeSettlement> TradeSettlements { get; }

    DbSet<Rebalance> Rebalances { get; }

    DbSet<MigrationBatch> MigrationBatches { get; }

    DbSet<Instrument> Instruments { get; }

    DbSet<InstrumentFifTreatment> InstrumentFifTreatments { get; }

    DbSet<Account> Accounts { get; }

    DbSet<Transaction> Transactions { get; }

    DbSet<TransactionCashLeg> TransactionCashLegs { get; }

    DbSet<TransactionSecurityLeg> TransactionSecurityLegs { get; }

    DbSet<Holding> Holdings { get; }

    DbSet<Portfolio> Portfolios { get; }

    DbSet<PortfolioLine> PortfolioLines { get; }

    DbSet<AccountPortfolioAssignment> AccountPortfolioAssignments { get; }

    DbSet<TradeFeeSchedule> TradeFeeSchedules { get; }

    DbSet<TradeFeeBand> TradeFeeBands { get; }

    DbSet<TransactionFeeDetail> TransactionFeeDetails { get; }

    DbSet<TransactionTaxDetail> TransactionTaxDetails { get; }

    DbSet<TransactionInterestDetail> TransactionInterestDetails { get; }

    DbSet<TransactionCorporateActionDetail> TransactionCorporateActionDetails { get; }

    DbSet<TransactionDividendDetail> TransactionDividendDetails { get; }

    DbSet<TransactionExecutionDetail> TransactionExecutionDetails { get; }

    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    DbSet<Report> Reports { get; }

    DbSet<TenantReport> TenantReports { get; }

    DbSet<ReportExecution> ReportExecutions { get; }

    DbSet<ReportSchedule> ReportSchedules { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="action"/> in one transaction at <paramref name="isolationLevel"/>, for example
    /// SNAPSHOT for a consistent read that blocks no writer (bank-reconciliation R6).</summary>
    Task ExecuteInTransactionAsync(
        System.Data.IsolationLevel isolationLevel,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);

    Task LockAccountAsync(int accountId, CancellationToken cancellationToken);

    void ClearTrackedChanges();

    Task LoadCashLegsAsync(Transaction transaction, CancellationToken cancellationToken);

    Task LoadSecurityLegsAsync(Transaction transaction, CancellationToken cancellationToken);
}
