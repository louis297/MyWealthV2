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
    public static byte[] Render(AdviserBalancesPdfModel model)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var asOf = Format(model.AsOf);
        var generated = Format(model.GeneratedAt);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(style => style.FontSize(9));
                page.Content().Column(column =>
                {
                    column.Spacing(4);
                    column.Item().Text("Adviser Balances Report").Bold().FontSize(16);
                    column.Item().Text($"Tenant: {model.TenantName}");
                    column.Item().Text($"Adviser: {model.AdviserName}");
                    column.Item().Text($"As of: {asOf} UTC");
                    column.Item().Text($"Generated: {generated} UTC");
                    column.Item().Text(
                        $"Include closed accounts: {(model.IncludeClosedAccounts ? "yes" : "no")}");
                    if (model.Rows.Count == 0)
                    {
                        column.Item().PaddingTop(12).Text("No balances.");
                    }
                    else
                    {
                        column.Item().PaddingTop(12).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(2);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });
                            table.Header(header =>
                            {
                                header.Cell().Text("Customer").Bold();
                                header.Cell().Text("Account").Bold();
                                header.Cell().Text("Type").Bold();
                                header.Cell().Text("Instrument").Bold();
                                header.Cell().Text("Quantity").Bold();
                                header.Cell().Text("Cost").Bold();
                                header.Cell().Text("Balance").Bold();
                                header.Cell().Text("Market value").Bold();
                            });
                            foreach (var row in model.Rows)
                            {
                                switch (row)
                                {
                                    case AdviserBalancesPdfCashRow cash:
                                        table.Cell().Text(cash.Customer);
                                        table.Cell().Text(cash.Account);
                                        table.Cell().Text(cash.AccountType);
                                        table.Cell().Text(string.Empty);
                                        table.Cell().Text(string.Empty);
                                        table.Cell().Text(string.Empty);
                                        table.Cell().Text(cash.Balance);
                                        table.Cell().Text(string.Empty);
                                        break;
                                    case AdviserBalancesPdfHoldingRow holding:
                                        table.Cell().Text(holding.Customer);
                                        table.Cell().Text(holding.Account);
                                        table.Cell().Text(string.Empty);
                                        table.Cell().Text(holding.Instrument);
                                        table.Cell().Text(holding.Quantity);
                                        table.Cell().Text(holding.Cost);
                                        table.Cell().Text(string.Empty);
                                        table.Cell().Text(holding.MarketValue);
                                        break;
                                }
                            }
                        });
                    }

                    if (model.Subtotals.Count > 0)
                    {
                        column.Item().PaddingTop(12).Text("Subtotals").Bold();
                        foreach (var subtotal in model.Subtotals)
                        {
                            column.Item().Text(
                                $"{subtotal.Currency} cash {subtotal.Cash} market value {subtotal.MarketValue}");
                        }
                    }
                });
            });
        }).GeneratePdf();
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
