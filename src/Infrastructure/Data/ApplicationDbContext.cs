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

    public DbSet<Instrument> Instruments => Set<Instrument>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<TransactionCashLeg> TransactionCashLegs => Set<TransactionCashLeg>();

    public DbSet<TransactionSecurityLeg> TransactionSecurityLegs => Set<TransactionSecurityLeg>();

    public DbSet<Holding> Holdings => Set<Holding>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<Report> Reports => Set<Report>();

    public DbSet<TenantReport> TenantReports => Set<TenantReport>();

    public DbSet<ReportExecution> ReportExecutions => Set<ReportExecution>();

    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();

    public DbSet<UserToken> UserTokenSeams => Set<UserToken>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<FxRate> FxRates => Set<FxRate>();

    public DbSet<InstrumentPrice> InstrumentPrices => Set<InstrumentPrice>();

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var strategy = Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
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

    public Task LoadCashLegAsync(Transaction transaction, CancellationToken cancellationToken) =>
        Entry(transaction).Reference(row => row.CashLeg).LoadAsync(cancellationToken);

    public Task LoadSecurityLegsAsync(Transaction transaction, CancellationToken cancellationToken) =>
        Entry(transaction).Collection(row => row.SecurityLegs).LoadAsync(cancellationToken);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            CaptureOutboxMessages();
            return await base.SaveChangesAsync(cancellationToken);
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
}
