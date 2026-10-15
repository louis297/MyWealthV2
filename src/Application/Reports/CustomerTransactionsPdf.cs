using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MyWealthV2.Application.Reports;

public sealed record CustomerTransactionsPdfRow(
    DateTimeOffset BookedAt,
    string AccountName,
    string Type,
    string Cash,
    string Instrument,
    string Quantity,
    string? Memo);

public sealed record CustomerTransactionsPdfModel(
    string TenantName,
    DateTimeOffset GeneratedAt,
    string CustomerName,
    DateOnly From,
    DateOnly To,
    bool IncludeClosedAccounts,
    IReadOnlyList<CustomerTransactionsPdfRow> Rows);

public static class CustomerTransactionsPdf
{
    public static byte[] Render(CustomerTransactionsPdfModel model)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var generated = model.GeneratedAt.ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(style => style.FontSize(10));
                page.Content().Column(column =>
                {
                    column.Spacing(4);
                    column.Item().Text("Customer Transactions Report").Bold().FontSize(16);
                    column.Item().Text($"Tenant: {model.TenantName}");
                    column.Item().Text($"Generated: {generated} UTC");
                    column.Item().Text($"Customer: {model.CustomerName}");
                    column.Item().Text($"From: {model.From:yyyy-MM-dd}");
                    column.Item().Text($"To: {model.To:yyyy-MM-dd}");
                    column.Item().Text(
                        $"Include closed accounts: {(model.IncludeClosedAccounts ? "yes" : "no")}");
                    if (model.Rows.Count == 0)
                    {
                        column.Item().PaddingTop(12).Text("No transactions in this range.");
                    }
                });
            });
        }).GeneratePdf();
    }
}
