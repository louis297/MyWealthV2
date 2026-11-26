using System.Data;
using System.Reflection;
using FluentValidation.Results;
using Microsoft.Data.SqlClient;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Common;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MyWealthV2.Infrastructure.Data;

public class ApplicationDbContext : IdentityUserContext<ApplicationUser>, IApplicationDbContext
{
    private readonly IServiceProvider? _services;
    private readonly ITenantIdAccessor? _tenantIds;

    public bool TenantFilterEngaged => _tenantIds?.IsEngaged == true;

    public int? CurrentTenantId => _tenantIds?.TenantId;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantIdAccessor tenantIds)
        : this(options)
    {
        _tenantIds = tenantIds;
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IServiceProvider services)
        : base(options)
    {
        _services = services;
    }

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IServiceProvider services,
        ITenantIdAccessor tenantIds)
        : this(options, tenantIds)
    {
        _services = services;
    }

    public DbSet<User> DomainUsers => Set<User>();

    DbSet<User> IApplicationDbContext.Users => DomainUsers;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<ClientMoneyAccount> ClientMoneyAccounts => Set<ClientMoneyAccount>();

    public DbSet<BankFeedCursor> BankFeedCursors => Set<BankFeedCursor>();

    public DbSet<MarketDataFeed> MarketDataFeeds => Set<MarketDataFeed>();

    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();

    public DbSet<BankReconciliation> BankReconciliations => Set<BankReconciliation>();

    public DbSet<BankReconciliationItem> BankReconciliationItems => Set<BankReconciliationItem>();

    public DbSet<NominatedBankAccount> NominatedBankAccounts => Set<NominatedBankAccount>();

    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();

    public DbSet<OutgoingPayment> OutgoingPayments => Set<OutgoingPayment>();

    public DbSet<AccountClosure> AccountClosures => Set<AccountClosure>();

    public DbSet<CustomerTaxProfile> CustomerTaxProfiles => Set<CustomerTaxProfile>();

    public DbSet<InterestRateSchedule> InterestRateSchedules => Set<InterestRateSchedule>();

    public DbSet<InterestRateBand> InterestRateBands => Set<InterestRateBand>();

    public DbSet<InterestAccrual> InterestAccruals => Set<InterestAccrual>();

    public DbSet<InterestRun> InterestRuns => Set<InterestRun>();

    public DbSet<ValuationRun> ValuationRuns => Set<ValuationRun>();

    public DbSet<MyWealthV2.Domain.Valuations.StoredValuation> StoredValuations => Set<MyWealthV2.Domain.Valuations.StoredValuation>();

    public DbSet<CorporateAction> CorporateActions => Set<CorporateAction>();

    public DbSet<CorporateActionEntitlement> CorporateActionEntitlements => Set<CorporateActionEntitlement>();

    public DbSet<CorporateActionAnnouncement> CorporateActionAnnouncements => Set<CorporateActionAnnouncement>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderFill> OrderFills => Set<OrderFill>();

    public DbSet<ExecutionReport> ExecutionReports => Set<ExecutionReport>();

    public DbSet<BrokerFeed> BrokerFeeds => Set<BrokerFeed>();

    public DbSet<SafekeepingAccount> SafekeepingAccounts => Set<SafekeepingAccount>();

    public DbSet<CustodianFeedCursor> CustodianFeedCursors => Set<CustodianFeedCursor>();

    public DbSet<CustodianPosition> CustodianPositions => Set<CustodianPosition>();

    public DbSet<CustodyMovement> CustodyMovements => Set<CustodyMovement>();

    public DbSet<InKindTransfer> InKindTransfers => Set<InKindTransfer>();

    public DbSet<AdjustmentRequest> AdjustmentRequests => Set<AdjustmentRequest>();

    public DbSet<CustodianReconciliation> CustodianReconciliations => Set<CustodianReconciliation>();

    public DbSet<TradeSettlement> TradeSettlements => Set<TradeSettlement>();

    public DbSet<Rebalance> Rebalances => Set<Rebalance>();
    public DbSet<MigrationBatch> MigrationBatches => Set<MigrationBatch>();

    public DbSet<Instrument> Instruments => Set<Instrument>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<TransactionCashLeg> TransactionCashLegs => Set<TransactionCashLeg>();

    public DbSet<TransactionSecurityLeg> TransactionSecurityLegs => Set<TransactionSecurityLeg>();

    public DbSet<Holding> Holdings => Set<Holding>();

    public DbSet<Portfolio> Portfolios => Set<Portfolio>();

    public DbSet<PortfolioLine> PortfolioLines => Set<PortfolioLine>();

    public DbSet<AccountPortfolioAssignment> AccountPortfolioAssignments => Set<AccountPortfolioAssignment>();

    public DbSet<TradeFeeSchedule> TradeFeeSchedules => Set<TradeFeeSchedule>();

    public DbSet<TradeFeeBand> TradeFeeBands => Set<TradeFeeBand>();

    public DbSet<TransactionFeeDetail> TransactionFeeDetails => Set<TransactionFeeDetail>();

    public DbSet<TransactionTaxDetail> TransactionTaxDetails => Set<TransactionTaxDetail>();

    public DbSet<TransactionInterestDetail> TransactionInterestDetails => Set<TransactionInterestDetail>();

    public DbSet<TransactionCorporateActionDetail> TransactionCorporateActionDetails => Set<TransactionCorporateActionDetail>();

    public DbSet<TransactionDividendDetail> TransactionDividendDetails => Set<TransactionDividendDetail>();

    public DbSet<TransactionExecutionDetail> TransactionExecutionDetails => Set<TransactionExecutionDetail>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<Report> Reports => Set<Report>();

    public DbSet<TenantReport> TenantReports => Set<TenantReport>();

    public DbSet<ReportExecution> ReportExecutions => Set<ReportExecution>();

    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();

    public DbSet<UserToken> UserTokenSeams => Set<UserToken>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<FxRate> FxRates => Set<FxRate>();

    public DbSet<InstrumentPrice> InstrumentPrices => Set<InstrumentPrice>();

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken) =>
        ExecuteInTransactionAsync(System.Data.IsolationLevel.ReadCommitted, action, cancellationToken);

    public async Task ExecuteInTransactionAsync(
        System.Data.IsolationLevel isolationLevel,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var strategy = Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(isolationLevel, cancellationToken);
            try
            {
                await action(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public void ClearTrackedChanges() => ChangeTracker.Clear();

    public async Task LockAccountAsync(int accountId, CancellationToken cancellationToken)
    {
        var connection = Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT 1 FROM [Accounts] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = accountId;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public Task LoadCashLegsAsync(Transaction transaction, CancellationToken cancellationToken) =>
        Entry(transaction).Collection(row => row.CashLegs).LoadAsync(cancellationToken);

    public Task LoadSecurityLegsAsync(Transaction transaction, CancellationToken cancellationToken) =>
        Entry(transaction).Collection(row => row.SecurityLegs).LoadAsync(cancellationToken);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            CaptureOutboxMessages();
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDepositReferenceTaken(exception))
        {
            throw new DepositReferenceTakenException(exception);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "A unique constraint was violated.")
            ]);
        }
    }

    private void CaptureOutboxMessages()
    {
        var entities = ChangeTracker.Entries<BaseEntity>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();
        if (entities.Count == 0)
        {
            return;
        }

        var now = (_services?.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        var createdBy = _services?.GetService<IUser>()?.Id;
        int? accessorTenantId = _tenantIds is { IsEngaged: true } ? _tenantIds.TenantId : null;

        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents.ToList())
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = domainEvent.GetType().FullName
                        ?? throw new InvalidOperationException("Domain event type has no full name."),
                    Payload = OutboxSerializer.Serialize(domainEvent),
                    OccurredAt = OutboxSerializer.OccurredAt(domainEvent, now),
                    TenantId = OutboxSerializer.TenantId(domainEvent, accessorTenantId),
                    Status = "Pending",
                    RetryCount = 0,
                    Created = now,
                    CreatedBy = createdBy
                });
            }

            entity.ClearDomainEvents();
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyTenantQueryFilters(builder);
    }

    private void ApplyTenantQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes().ToList())
        {
            var clrType = entityType.ClrType;
            if (clrType is null
                || clrType == typeof(ApplicationUser)
                || clrType == typeof(OutboxMessage)
                || entityType.BaseType is not null
                || clrType.Namespace?.StartsWith("OpenIddict", StringComparison.Ordinal) == true)
            {
                continue;
            }

            var tenantId = entityType.FindProperty("TenantId");
            if (tenantId?.ClrType != typeof(int) && tenantId?.ClrType != typeof(int?))
            {
                continue;
            }

            typeof(ApplicationDbContext)
                .GetMethod(nameof(ApplyTenantQueryFilter), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(clrType)
                .Invoke(this, [builder, tenantId.ClrType == typeof(int?)]);
        }
    }

    private void ApplyTenantQueryFilter<TEntity>(ModelBuilder builder, bool nullableTenantId)
        where TEntity : class
    {
        if (nullableTenantId)
        {
            builder.Entity<TEntity>().HasQueryFilter(entity =>
                !TenantFilterEngaged || EF.Property<int?>(entity, "TenantId") == CurrentTenantId);
            return;
        }

        builder.Entity<TEntity>().HasQueryFilter(entity =>
            !TenantFilterEngaged || EF.Property<int>(entity, "TenantId") == CurrentTenantId);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && sql.Number is 2601 or 2627;

    /// <summary>A unique violation on <c>UX_Accounts_DepositReference</c> and nothing else (client-money-accounts R17 b).</summary>
    private static bool IsDepositReferenceTaken(DbUpdateException exception) =>
        IsUniqueConstraintViolation(exception)
        && exception.InnerException!.Message.Contains("UX_Accounts_DepositReference", StringComparison.Ordinal);
}
