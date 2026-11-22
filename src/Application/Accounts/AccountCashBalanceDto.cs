namespace MyWealthV2.Application.Accounts;

/// <summary>The cash of one Account: the balance, the cash reserved for open withdrawals and what is available
/// (withdrawals R35).</summary>
public sealed record AccountCashBalanceDto(
    Guid AccountId,
    string Currency,
    decimal CashBalance,
    decimal ReservedCash,
    decimal AvailableCash,
    decimal UnsettledCash = 0m,
    decimal WithdrawableCash = 0m);
