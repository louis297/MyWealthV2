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
        string currency)
    {
        var instrument = await db.Instruments.SingleAsync(row => row.PublicId == instrumentPublicId);
        if (!string.Equals(instrument.QuoteCurrency.Trim(), currency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Test price currency must match the instrument.");
        }

        var asOf = new DateOnly(2026, 1, 1);
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
        row.Source = "TwelveData";
        row.FetchedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await db.SaveChangesAsync();
    }
}
