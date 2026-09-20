using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.MarketData;

namespace MyWealthV2.Infrastructure.Data;

public static class TestSeed
{
    public const string CreatedBy = "system";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var prices = services.GetRequiredService<MarketDataMock>();
        await SeedAsync(db, prices, cancellationToken);
    }

    public static async Task SeedAsync(
        ApplicationDbContext db,
        MarketDataMock prices,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(
            row => row.Code == DevelopmentIdentitySeeder.DemoTenantCode, cancellationToken);
        if (tenant is null)
        {
            return;
        }

        var usd = Currency.Create("USD", "United States Dollar", 2);
        var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);

        var vti = await EnsureInstrumentAsync(
            db, tenant.Id, "VTI", "Vanguard Total Stock Market ETF", usd, enabled: true, cancellationToken);
        var aia = await EnsureInstrumentAsync(
            db, tenant.Id, "AIA", "Auckland International Airport", nzd, enabled: true, cancellationToken);
        await EnsureInstrumentAsync(
            db, tenant.Id, "NZD-CASH", "Demo disabled cash proxy", nzd, enabled: false, cancellationToken);

        prices.SetPrice(vti.Id, Money.Create(250m, "USD"));
        prices.SetPrice(aia.Id, Money.Create(8.50m, "NZD"));
    }

    private static async Task<Instrument> EnsureInstrumentAsync(
        ApplicationDbContext db,
        int tenantId,
        string symbol,
        string name,
        Currency quoteCurrency,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var existing = await db.Instruments.SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.Symbol == symbol, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var instrument = Instrument.Create(tenantId, symbol, name, quoteCurrency);
        if (!enabled)
        {
            instrument.Disable();
        }

        instrument.Created = TimeProvider.System.GetUtcNow();
        instrument.CreatedBy = CreatedBy;
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);
        return instrument;
    }
}
