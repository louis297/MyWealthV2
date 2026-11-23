using Microsoft.EntityFrameworkCore;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Application.FunctionalTests;

internal static class TestPrices
{
    public static async Task UpsertAsync(
        ApplicationDbContext db,
        Guid instrumentPublicId,
        decimal amount,
        string currency,
        DateOnly? asOfDate = null)
    {
        var instrument = await db.Instruments.SingleAsync(row => row.PublicId == instrumentPublicId);
        if (!string.Equals(instrument.QuoteCurrency.Trim(), currency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Test price currency must match the instrument.");
        }

        // By default today's UTC date, so the price is fresh for an order or a rebalance (orders R10, rebalances R8).
        var asOf = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var row = await db.InstrumentPrices.SingleOrDefaultAsync(price =>
            price.InstrumentId == instrument.Id && price.AsOfDate == asOf);
        if (row is null)
        {
            row = new InstrumentPrice
            {
                InstrumentId = instrument.Id,
                AsOfDate = asOf
            };
            db.InstrumentPrices.Add(row);
        }

        row.Price = amount;
        row.Source = "EODHD";
        row.FetchedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }
}
