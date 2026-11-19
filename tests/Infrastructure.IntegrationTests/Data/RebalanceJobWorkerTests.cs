using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Accounts;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Shared;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>
/// The rebalance job as the Worker runs it: Infrastructure services with <see cref="SystemUser"/> as the
/// <see cref="IUser"/>. Its trades carry the system name in <c>CreatedBy</c> (database-design §3: the actor's
/// Identity id or a system name) and the rebalance handle as their source (transaction-source R5).
/// </summary>
public class RebalanceJobWorkerTests
{
    private const decimal Price = 10m;

    [Test]
    public async Task Execute_AsTheWorker_PostsTheTradesWithTheSystemNameAndTheHandle()
    {
        var (accountId, instrumentId) = await InsertAssignedCustodialAccountAsync();
        var handle = Guid.NewGuid();
        using var host = WorkerHost(instrumentId);

        await Should.NotThrowAsync(async () =>
        {
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RebalanceJob>().Execute(accountId, handle);
        });

        await using var db = OpenDb();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        var trades = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Type == TransactionType.Buy && row.CashLegs.Any(leg => leg.AccountId == account.Id))
            .ToListAsync();
        trades.ShouldNotBeEmpty();
        trades.ShouldAllBe(row => row.CreatedBy == SystemUser.Name);
        trades.ShouldAllBe(row => row.Source.Kind == TransactionSourceKind.Rebalance && row.Source.PublicId == handle);
    }

    private static IHost WorkerHost(int pricedInstrumentId)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] = SchemaDatabaseSetup.ConnectionString;
        builder.AddInfrastructureServices();
        builder.Services.AddScoped<IUser, SystemUser>();
        // The last registration wins: a fixed price instead of the stored instrument prices.
        builder.Services.AddScoped<IMarketData>(_ => new FixedPrice(pricedInstrumentId));
        return builder.Build();
    }

    /// <summary>A funded Open Custodial NZD account with an active 100 % assignment on one NZD instrument.</summary>
    private static async Task<(Guid AccountId, int InstrumentId)> InsertAssignedCustodialAccountAsync()
    {
        await using var db = OpenDb();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var (adviser, otherAdviser) = await TwoTenantFixture.InsertAdvisersAsync(db, tenant, otherTenant);
        var (customer, _) = await TwoTenantFixture.InsertCustomersAsync(db, tenant, otherTenant, adviser, otherAdviser);
        var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);

        var account = Account.Create(tenant.Id, customer.Id, "Model", AccountType.Brokerage, CustodyMode.Custodial, nzd,
            DepositReference.Generate(CryptoRandomSymbols.Instance));
        account.CreatedBy = "test";
        var instrument = Instrument.Create(tenant.Id, "AIA", "AIA fund", nzd);
        instrument.CreatedBy = "test";
        db.Accounts.Add(account);
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync();

        var portfolio = Portfolio.Create(tenant.Id, "Growth", nzd, [(instrument.Id, 100m)]);
        portfolio.CreatedBy = "test";
        db.Portfolios.Add(portfolio);
        var funding = Transaction.Post(
            tenant.Id, account.Id, TransactionType.TransferIn, 1000m, "NZD", DateTimeOffset.UtcNow.AddDays(-1),
            memo: null, reference: null, TransactionSource.Seed, currentCashSum: 0m, existingTransactionCount: 0);
        funding.CreatedBy = "test";
        funding.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(funding);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var assignment = AccountPortfolioAssignment.Assign(null, tenant.Id, account.Id, portfolio.Id, "test", now);
        assignment.Created = now;
        assignment.CreatedBy = "test";
        db.AccountPortfolioAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return (account.PublicId, instrument.Id);
    }

    private static ApplicationDbContext OpenDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FixedPrice(int instrumentId) : IMarketData
    {
        public Money? TryGetPrice(int id, DateTimeOffset? asOf = null) =>
            id == instrumentId ? Money.Create(Price, "NZD") : null;
    }
}
