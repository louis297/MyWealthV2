namespace MyWealthV2.Application.Accounts;

public sealed class AccountHoldingsDto
{
    public required IReadOnlyList<AccountHoldingDto> Items { get; init; }
}
