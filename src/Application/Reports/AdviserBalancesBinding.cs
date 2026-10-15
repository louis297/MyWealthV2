namespace MyWealthV2.Application.Reports;

public sealed record AdviserBalancesBinding(
    Guid AdviserId,
    bool IncludeClosedAccounts,
    string SnapshotJson);
