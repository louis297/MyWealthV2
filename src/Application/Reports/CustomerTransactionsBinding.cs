namespace MyWealthV2.Application.Reports;

public sealed record CustomerTransactionsBinding(
    Guid CustomerId,
    DateOnly From,
    DateOnly To,
    bool IncludeClosedAccounts,
    string SnapshotJson);
