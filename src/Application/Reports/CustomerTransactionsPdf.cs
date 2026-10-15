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
        _ = model;
        return [];
    }
}
