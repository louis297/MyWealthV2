using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Portfolios;
using MyWealthV2.Application.Portfolios.Commands.RebalanceAccount;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Tenant composite keys B3: RebalanceJob engages the tenant filter with the Account's tenant.</summary>
public class RebalanceJobTenantScopeTests
{
    [Test]
    public async Task Execute_EngagesTheAccountTenant()
    {
        var accessor = new TenantIdAccessor();
        await using var db = OpenDb(accessor);
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var (adviserA, _) = await TwoTenantFixture.InsertAdvisersAsync(db, tenantA, tenantB);
        var customer = User.Create(UserRole.Customer, "Customer", $"c-{Guid.NewGuid():N}@example", await IdentityAsync(db, tenantA), tenantA.Id, adviserA.Id);
        db.DomainUsers.Add(customer);
        await db.SaveChangesAsync();
        var account = Account.Create(tenantA.Id, customer.Id, "Brokerage", AccountType.Brokerage, Currency.Create("NZD", "New Zealand Dollar", 2));
        account.CreatedBy = "test";
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var job = NewJob(db, accessor);

        // The account has no portfolio assignment, so the rebalancer refuses it after loading it.
        await Should.ThrowAsync<RebalanceException>(() => job.Execute(account.PublicId, Guid.NewGuid()));

        accessor.IsEngaged.ShouldBeTrue();
        accessor.TenantId.ShouldBe(tenantA.Id);
    }

    [Test]
    public async Task Execute_UnknownAccount_ThrowsNotFoundAsBefore()
    {
        var accessor = new TenantIdAccessor();
        await using var db = OpenDb(accessor);

        var job = NewJob(db, accessor);

        await Should.ThrowAsync<NotFoundException>(() => job.Execute(Guid.NewGuid(), Guid.NewGuid()));
        accessor.IsEngaged.ShouldBeFalse();
    }

    private static RebalanceJob NewJob(ApplicationDbContext db, TenantIdAccessor accessor) =>
        new(new AccountRebalancer(db, new NoMarketData(), new NoFxRate(), TimeProvider.System), db, accessor);

    private static async Task<string> IdentityAsync(ApplicationDbContext db, Tenant tenant)
    {
        var identity = new MyWealthV2.Infrastructure.Identity.ApplicationUser
        {
            UserName = Guid.NewGuid().ToString(),
            Email = $"{Guid.NewGuid():N}@example",
            EmailConfirmed = true,
            TenantId = tenant.Id
        };
        db.Users.Add(identity);
        await db.SaveChangesAsync();
        return identity.Id;
    }

    private static ApplicationDbContext OpenDb(TenantIdAccessor accessor)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options, accessor);
    }

    private sealed class NoMarketData : IMarketData
    {
        public Money? TryGetPrice(int instrumentId, DateTimeOffset? asOf = null) => null;
    }

    private sealed class NoFxRate : IFxRate
    {
        public Task<decimal> GetRateAsync(string from, string to, DateTimeOffset? asOf = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not used.");

        public Task<FxRateReading> GetRateDetailAsync(string from, string to, DateTimeOffset asOf, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not used.");
    }
}
