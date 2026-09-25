namespace MyWealthV2.Application.Accounts;

public sealed record AccountCashBalanceDto(Guid AccountId, string Currency, decimal CashBalance);
