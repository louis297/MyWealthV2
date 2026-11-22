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
    string? Memo,
    string Fee = "",
    string Custody = "",
    string Tax = "");

public sealed record CustomerTransactionsPdfModel(
    string TenantName,
    DateTimeOffset GeneratedAt,
    string CustomerName,
    DateOnly From,
    DateOnly To,
    bool IncludeClosedAccounts,
    IReadOnlyList<CustomerTransactionsPdfRow> Rows,
    bool ShowExternalNote = false);

public static class CustomerTransactionsPdf
{
    /// <summary>The note under the table when a row is on an External account (custody-mode-grouping R15).</summary>
    public const string ExternalNote =
        "Held elsewhere: recorded by your adviser from outside statements. The platform has not checked these figures.";

    public static byte[] Render(CustomerTransactionsPdfModel model)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var generated = model.GeneratedAt.ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

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
                    else
                    {
                        column.Item().PaddingTop(12).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn(2);
                                columns.RelativeColumn();
                            });
                            table.Header(header =>
                            {
                                header.Cell().Text("Booked at").Bold();
                                header.Cell().Text("Account").Bold();
                                header.Cell().Text("Custody").Bold();
                                header.Cell().Text("Type").Bold();
                                header.Cell().Text("Cash").Bold();
                                header.Cell().Text("Fee").Bold();
                                header.Cell().Text("Instrument").Bold();
                                header.Cell().Text("Quantity").Bold();
                                header.Cell().Text("Memo").Bold();
                            });
                            foreach (var row in model.Rows)
                            {
                                var booked = row.BookedAt.ToUniversalTime()
                                    .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                                table.Cell().Text(booked);
                                table.Cell().Text(row.AccountName);
                                table.Cell().Text(row.Custody);
                                table.Cell().Text(row.Type);
                                table.Cell().Text(row.Cash);
                                table.Cell().Text(row.Fee);
                                table.Cell().Text(row.Instrument);
                                table.Cell().Text(row.Quantity);
                                table.Cell().Text(row.Memo ?? string.Empty);
                            }
                        });

                        if (model.ShowExternalNote)
                        {
                            column.Item().PaddingTop(8).Text(ExternalNote);
                        }
                    }
                });
            });
        }).GeneratePdf();
    }
}
