using Microsoft.EntityFrameworkCore;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>
/// Valuation run §9.1: the seeded history that gives a valuation run in Development more than one day to value.
/// Prices on every weekday and rates on every day from 2026-01-02 to 2026-03-31, by a fixed pattern from the
/// 2026-01-01 row that ends on it again; a Sell and a TransferIn on the demo Custodial account, dated from its funding.
/// </summary>
[Parallelizable]
public class TestSeedValuationTests
{
    private static readonly DateOnly From = new(2026, 1, 2);
    private static readonly DateOnly To = new(2026, 3, 31);

    private static IEnumerable<DateOnly> Days() =>
        Enumerable.Range(0, To.DayNumber - From.DayNumber + 1).Select(From.AddDays);

    private static IEnumerable<DateOnly> Weekdays() =>
        Days().Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday);

    [Test]
    public async Task Seed_WritesAWeekdayPriceHistory_ForEveryPricedInstrumentThatIsNotSimulated()
    {
        await using var db = OpenDb();
        var tenant = await EnsureDemoTenantAsync(db);
        await TestSeed.SeedAsync(db, CancellationToken.None);
        await TestSeed.SeedAsync(db, CancellationToken.None);

        var instruments = await db.Instruments.AsNoTracking().Where(row => row.TenantId == tenant.Id).ToListAsync();
        var ids = instruments.Select(row => row.Id).ToList();
        var rows = await db.InstrumentPrices.AsNoTracking().Where(row => ids.Contains(row.InstrumentId)).ToListAsync();
        foreach (var instrument in instruments)
        {
            var own = rows.Where(row => row.InstrumentId == instrument.Id).ToList();
            var opening = own.SingleOrDefault(row => row.AsOfDate == new DateOnly(2026, 1, 1));
            var history = own.Where(row => row.AsOfDate >= From && row.AsOfDate <= To).OrderBy(row => row.AsOfDate).ToList();
            if (opening is null || instrument.Symbol.EndsWith(".SIM", StringComparison.Ordinal))
            {
                history.ShouldBeEmpty(instrument.Symbol);
                continue;
            }

            history.Select(row => row.AsOfDate).ShouldBe(Weekdays(), instrument.Symbol);
            history.ShouldAllBe(row => row.Source == "EODHD" && row.Price > 0m);
            history.Last().Price.ShouldBe(opening.Price, $"{instrument.Symbol} ends on its opening price");
        }

        var vti = instruments.Single(row => row.Symbol == "VTI");
        Price(rows, vti, new DateOnly(2026, 2, 16)).ShouldBe(252.50m);
        Price(rows, vti, new DateOnly(2026, 1, 19)).ShouldBe(247.50m);
        Price(rows, vti, new DateOnly(2026, 3, 13)).ShouldBe(255.00m);
    }

    [Test]
    public async Task Seed_WritesADailyRateHistory_ForEveryPairOfTheFirstOfJanuary()
    {
        await using var db = OpenDb();
        await EnsureDemoTenantAsync(db);
        await TestSeed.SeedAsync(db, CancellationToken.None);
        await TestSeed.SeedAsync(db, CancellationToken.None);

        var rows = await db.FxRates.AsNoTracking().ToListAsync();
        var pairs = rows.Where(row => row.AsOfDate == new DateOnly(2026, 1, 1))
            .Select(row => (Base: row.BaseCurrency.Trim(), Quote: row.QuoteCurrency.Trim(), row.Rate)).ToList();
        pairs.Count.ShouldBeGreaterThanOrEqualTo(10);
        foreach (var pair in pairs)
        {
            var history = rows.Where(row => row.BaseCurrency.Trim() == pair.Base && row.QuoteCurrency.Trim() == pair.Quote
                                            && row.AsOfDate >= From && row.AsOfDate <= To)
                .OrderBy(row => row.AsOfDate).ToList();
            history.Select(row => row.AsOfDate).ShouldBe(Days(), $"{pair.Base}/{pair.Quote}");
            history.ShouldAllBe(row => row.Source == "Frankfurter" && row.Rate > 0m);
            history.Last().Rate.ShouldBe(pair.Rate);
        }

        Rate(rows, "USD", "NZD", new DateOnly(2026, 2, 16)).ShouldBe(1.51500000m);
        Rate(rows, "NZD", "USD", new DateOnly(2026, 2, 16)).ShouldBe(0.66006601m);
    }

    [Test]
    public async Task Seed_KeepsAPriceOrRateRowThatIsAlreadyStored()
    {
        await using var db = OpenDb();
        var tenant = await EnsureDemoTenantAsync(db);
        await TestSeed.SeedAsync(db, CancellationToken.None);
        var vti = await db.Instruments.SingleAsync(row => row.TenantId == tenant.Id && row.Symbol == "VTI");
        var day = new DateOnly(2026, 1, 6);
        await db.InstrumentPrices.Where(row => row.InstrumentId == vti.Id && row.AsOfDate == day).ExecuteDeleteAsync();
        await db.FxRates.Where(row => row.BaseCurrency == "USD" && row.QuoteCurrency == "AUD" && row.AsOfDate == day).ExecuteDeleteAsync();
        db.InstrumentPrices.Add(new InstrumentPrice
        {
            InstrumentId = vti.Id, Price = 1.23m, AsOfDate = day, Source = "EODHD", FetchedAt = DateTimeOffset.UtcNow
        });
        db.FxRates.Add(new FxRate
        {
            BaseCurrency = "USD", QuoteCurrency = "AUD", Rate = 9.87m, AsOfDate = day, Source = "Frankfurter", FetchedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        try
        {
            await TestSeed.SeedAsync(db, CancellationToken.None);

            (await db.InstrumentPrices.AsNoTracking().SingleAsync(row => row.InstrumentId == vti.Id && row.AsOfDate == day))
                .Price.ShouldBe(1.23m);
            (await db.FxRates.AsNoTracking().SingleAsync(row => row.BaseCurrency == "USD" && row.QuoteCurrency == "AUD" && row.AsOfDate == day))
                .Rate.ShouldBe(9.87m);
        }
        finally
        {
            await db.InstrumentPrices.Where(row => row.InstrumentId == vti.Id && row.AsOfDate == day).ExecuteDeleteAsync();
            await db.FxRates.Where(row => row.BaseCurrency == "USD" && row.QuoteCurrency == "AUD" && row.AsOfDate == day).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Seed_DatesTheDemoCustodialAccountFromItsFunding_AndAddsASellOfVtiAndATransferIn()
    {
        await using var db = OpenDb();
        var tenant = await EnsureDemoTenantAsync(db);
        var adviser = await EnsurePersonAsync(db, tenant, UserRole.Adviser, "Demo Adviser", DevelopmentIdentitySeeder.AdviserEmail);
        await EnsurePersonAsync(db, tenant, UserRole.Customer, "Demo Customer", "customer@localhost", adviser.Id);
        await TestSeed.SeedAsync(db, CancellationToken.None);
        await TestSeed.SeedAsync(db, CancellationToken.None);

        var account = await db.Accounts.AsNoTracking()
            .SingleAsync(row => row.TenantId == tenant.Id && row.Name == "Demo custodial brokerage");
        LedgerDay.Of(account.Created).ShouldBe(new DateOnly(2026, 1, 19));
        var vti = await db.Instruments.SingleAsync(row => row.TenantId == tenant.Id && row.Symbol == "VTI");
        var nzdt = TimeSpan.FromHours(13);

        var sell = await TransactionAsync(db, account.Id, "TestSeed-DemoCustodial-SellVti");
        sell.Type.ShouldBe(TransactionType.Sell);
        sell.Source.Kind.ShouldBe(TransactionSourceKind.Seed);
        sell.BookedAt.ShouldBe(new DateTimeOffset(2026, 2, 17, 10, 0, 0, nzdt));
        LedgerDay.Of(sell.BookedAt).ShouldBe(new DateOnly(2026, 2, 16));
        sell.CashLegs.Single().Amount.ShouldBe(2_525.00m);
        sell.SecurityLegs.Single().ShouldSatisfyAllConditions(
            leg => leg.InstrumentId.ShouldBe(vti.Id),
            leg => leg.Quantity.ShouldBe(-10m),
            leg => leg.CostAmount.ShouldBe(-2_500.00m));

        var transfer = await TransactionAsync(db, account.Id, "TestSeed-DemoCustodial-TransferIn");
        transfer.Type.ShouldBe(TransactionType.TransferIn);
        transfer.Source.Kind.ShouldBe(TransactionSourceKind.Seed);
        LedgerDay.Of(transfer.BookedAt).ShouldBe(new DateOnly(2026, 3, 2));
        transfer.CashLegs.Single().Amount.ShouldBe(5_000.00m);

        (await db.TransactionCashLegs.Where(row => row.AccountId == account.Id).SumAsync(row => row.Amount)).ShouldBe(13_925.00m);
        var holding = await db.Holdings.AsNoTracking().SingleAsync(row => row.AccountId == account.Id && row.InstrumentId == vti.Id);
        (holding.Quantity, holding.CostAmount).ShouldBe((30m, 7_500m));
    }

    [Test]
    public async Task Seed_StoresNoValuation_RunOrValuedThroughDate()
    {
        await using var db = OpenDb();
        var tenant = await EnsureDemoTenantAsync(db);
        var adviser = await EnsurePersonAsync(db, tenant, UserRole.Adviser, "Demo Adviser", DevelopmentIdentitySeeder.AdviserEmail);
        await EnsurePersonAsync(db, tenant, UserRole.Customer, "Demo Customer", "customer@localhost", adviser.Id);
        await TestSeed.SeedAsync(db, CancellationToken.None);

        (await db.ValuationRuns.CountAsync(row => row.TenantId == tenant.Id)).ShouldBe(0);
        (await db.StoredValuations.CountAsync(row => row.TenantId == tenant.Id)).ShouldBe(0);
        (await db.Accounts.CountAsync(row => row.TenantId == tenant.Id && row.ValuedThrough != null)).ShouldBe(0);
    }

    private static decimal Price(List<InstrumentPrice> rows, Instrument instrument, DateOnly day) =>
        rows.Single(row => row.InstrumentId == instrument.Id && row.AsOfDate == day).Price;

    private static decimal Rate(List<FxRate> rows, string baseCurrency, string quoteCurrency, DateOnly day) =>
        rows.Single(row => row.BaseCurrency.Trim() == baseCurrency && row.QuoteCurrency.Trim() == quoteCurrency && row.AsOfDate == day).Rate;

    private static Task<Transaction> TransactionAsync(ApplicationDbContext db, int accountId, string reference) =>
        db.Transactions.AsNoTracking().Include(row => row.CashLegs).Include(row => row.SecurityLegs)
            .SingleAsync(row => row.Reference == reference && row.CashLegs.Any(leg => leg.AccountId == accountId));

    private static async Task<Tenant> EnsureDemoTenantAsync(ApplicationDbContext db)
    {
        var existing = await db.Tenants.SingleOrDefaultAsync(row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode);
        if (existing is not null)
        {
            return existing;
        }

        var tenant = Tenant.Create(
            DevelopmentIdentitySeeder.DemoTenantName,
            DevelopmentIdentitySeeder.DemoTenantCode,
            Currency.Create("NZD", "New Zealand Dollar", 2));
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<User> EnsurePersonAsync(
        ApplicationDbContext db,
        Tenant tenant,
        UserRole role,
        string name,
        string email,
        int? adviserId = null)
    {
        var existing = await db.DomainUsers.SingleOrDefaultAsync(row => row.TenantId == tenant.Id && row.Email == email);
        if (existing is not null)
        {
            return existing;
        }

        var publicId = Guid.NewGuid();
        var identity = new ApplicationUser
        {
            UserName = publicId.ToString(),
            Email = email,
            EmailConfirmed = true,
            TenantId = tenant.Id
        };
        db.Users.Add(identity);
        await db.SaveChangesAsync();

        var person = User.Create(role, name, email, identity.Id, tenant.Id, adviserId, publicId: publicId);
        db.DomainUsers.Add(person);
        await db.SaveChangesAsync();
        return person;
    }

    private static ApplicationDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(SchemaDatabaseSetup.ConnectionString).Options);
}
