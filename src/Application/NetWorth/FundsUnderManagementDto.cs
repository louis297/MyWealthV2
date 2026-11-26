namespace MyWealthV2.Application.NetWorth;

/// <summary>The funds under management series of a scope (valuation-history R12).</summary>
public sealed record FundsUnderManagementDto(
    string ReportingCurrency,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<FundsUnderManagementDayDto> Days);

/// <summary>One day of the series: per currency and folded (R5–R7, R10, R11).</summary>
public sealed record FundsUnderManagementDayDto(
    DateOnly Date,
    IReadOnlyList<FoldLineDto> Currencies,
    decimal Total,
    bool Complete,
    int Accounts,
    int IncompleteAccounts,
    int MissingAccounts);
