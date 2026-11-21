using System.Globalization;
using MyWealthV2.Application.BankReconciliations;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MyWealthV2.Application.Reports;

public sealed record BankReconciliationPdfModel(
    string TenantName,
    DateTimeOffset GeneratedAt,
    BankReconciliationDto Run,
    bool NoBankBalance,
    int DecimalPlaces);

/// <summary>Bank-reconciliation R25: the header, the two sides, the items by class with their totals, and the
/// information of R14. Labels follow the Adviser Portal page (D51).</summary>
public static class BankReconciliationPdf
{
    public const string NoBankBalanceSentence = "No bank balance yet.";

    private static readonly IReadOnlyDictionary<string, string> KindLabels = new Dictionary<string, string>
    {
        ["Unexplained"] = "Not explained",
        ["StatementDifference"] = "Bank balance differs from the statement lines",
        ["Suspense"] = "In suspense",
        ["NotProcessed"] = "Not processed yet",
        ["ReturnInProgress"] = "Being returned to the payer",
        ["OpeningBalance"] = "Balance before the first statement line",
        ["Ignored"] = "Ignored",
        ["Trades"] = "Trades not settled through the trust account",
        ["FxConversions"] = "FX conversions not settled through the trust account",
        ["Adjustments"] = "Adjustments",
        ["OtherLedgerCash"] = "Other cash entered on the platform",
        ["OtherClientMoneyAccount"] = "Cash from another client money account"
    };

    private static readonly (string Class, string Heading)[] Classes =
        [("Break", "Breaks"), ("Timing", "Timing"), ("Known", "Known differences")];

    public static byte[] Render(BankReconciliationPdfModel model)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var run = model.Run;
        var account = run.ClientMoneyAccount;
        string Money(decimal? amount) => amount is decimal value
            ? CashAmountText.Format(value, account.Currency, model.DecimalPlaces)
            : "-";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(style => style.FontSize(9));
                page.Content().Column(column =>
                {
                    column.Spacing(4);
                    column.Item().Text("Bank reconciliation").Bold().FontSize(16);
                    column.Item().Text($"Tenant: {model.TenantName}");
                    column.Item().Text($"Generated: {Format(model.GeneratedAt)} UTC");
                    column.Item().Text(
                        $"Client money account: {account.BankName}, {account.AccountName}, {account.AccountNumber}, {account.Currency}");
                    column.Item().Text($"Ran at: {Format(run.RanAt)} UTC");
                    column.Item().Text($"Trigger: {run.Trigger}");
                    column.Item().Text($"Status: {run.Status}");

                    if (model.NoBankBalance)
                    {
                        column.Item().PaddingTop(12).Text(NoBankBalanceSentence);
                        column.Item().Text($"Custodial cash: {Money(run.CustodialCash)}");
                        return;
                    }

                    column.Item().PaddingTop(12).Text(
                        $"Bank balance: {Money(run.BankBalance)} (statement read {Format(run.BalanceAt!.Value)} UTC)");
                    column.Item().Text($"Opening balance: {Money(run.OpeningBalance)}");
                    column.Item().Text($"Custodial cash: {Money(run.CustodialCash)}");
                    column.Item().Text($"Difference: {Money(run.Difference)}");

                    foreach (var (itemClass, heading) in Classes)
                    {
                        var items = run.Items.Where(item => item.Class == itemClass).ToList();
                        var total = itemClass switch
                        {
                            "Break" => run.Totals.Breaks,
                            "Timing" => run.Totals.Timing,
                            _ => run.Totals.Known
                        };
                        column.Item().PaddingTop(10).Text($"{heading}: {total.Count}, {Money(total.Amount)}").Bold();
                        foreach (var item in items)
                        {
                            column.Item().Text(Line(item, Money));
                        }
                    }

                    column.Item().PaddingTop(10).Text($"Reserved cash: {Money(run.ReservedCash)}");
                    column.Item().Text(
                        $"Payments in flight: {run.PaymentsInFlight.Count}, {Money(run.PaymentsInFlight.Amount)}");
                    var feed = run.Feed;
                    column.Item().Text(
                        $"Bank feed: last read {Format(feed.LastSucceededAt)}, last failure {Format(feed.LastFailedAt)}"
                        + (feed.LastFailure is null ? string.Empty : $" ({feed.LastFailure})"));
                });
            });
        }).GeneratePdf();
    }

    private static string Line(BankReconciliationItemDto item, Func<decimal?, string> money)
    {
        var label = KindLabels.GetValueOrDefault(item.Kind, item.Kind);
        var text = $"{label}: {money(item.Amount)}";
        if (item.StatementLine is { } line)
        {
            text += $", {line.Direction}, {line.ValueDate}, {line.Status}";
            if (line.UnmatchedReason is not null)
            {
                text += $", {line.UnmatchedReason}";
            }

            if (line.Note is not null)
            {
                text += $", {line.Note}";
            }
        }
        else if (item.Count > 1)
        {
            text += $", {item.Count} transactions";
        }

        return text;
    }

    private static string Format(DateTimeOffset? at) =>
        at?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "never";
}
