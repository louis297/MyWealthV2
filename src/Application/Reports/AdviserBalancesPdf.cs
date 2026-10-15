using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MyWealthV2.Application.Reports;

public abstract record AdviserBalancesPdfRow;

public sealed record AdviserBalancesPdfCashRow(
    string Customer,
    string Account,
    string AccountType,
    string Balance) : AdviserBalancesPdfRow;

public sealed record AdviserBalancesPdfHoldingRow(
    string Customer,
    string Account,
    string Instrument,
    string Quantity,
    string Cost,
    string MarketValue) : AdviserBalancesPdfRow;

public sealed record AdviserBalancesPdfSubtotal(string Currency, string Cash, string MarketValue);

public sealed record AdviserBalancesPdfModel(
    string TenantName,
    string AdviserName,
    DateTimeOffset AsOf,
    DateTimeOffset GeneratedAt,
    bool IncludeClosedAccounts,
    IReadOnlyList<AdviserBalancesPdfRow> Rows,
    IReadOnlyList<AdviserBalancesPdfSubtotal> Subtotals);

public static class AdviserBalancesPdf
{
    public static byte[] Render(AdviserBalancesPdfModel model) =>
        throw new NotImplementedException();
}
