namespace MyWealthV2.Application.NetWorth;

public sealed class NetWorthDto
{
    public required DateTimeOffset AsOf { get; init; }

    public required IReadOnlyList<NetWorthCurrencyDto> Currencies { get; init; }

    public required NetWorthBreakdownsDto Breakdowns { get; init; }

    public static NetWorthDto Empty(DateTimeOffset asOf) => new()
    {
        AsOf = asOf,
        Currencies = [],
        Breakdowns = new NetWorthBreakdownsDto
        {
            ByCustomer = [],
            ByAccountType = [],
            TopHoldings = []
        }
    };
}

public sealed class NetWorthCurrencyDto
{
    public required string Currency { get; init; }

    public required NetWorthAssetsDto Assets { get; init; }

    public required NetWorthLiabilitiesDto Liabilities { get; init; }

    public required decimal Net { get; init; }
}

public sealed class NetWorthAssetsDto
{
    public required decimal Cash { get; init; }

    public required decimal Holdings { get; init; }

    public required decimal Total { get; init; }
}

public sealed class NetWorthLiabilitiesDto
{
    public required decimal Total { get; init; }
}

public sealed class NetWorthBreakdownsDto
{
    public required IReadOnlyList<NetWorthCustomerBreakdownDto> ByCustomer { get; init; }

    public required IReadOnlyList<NetWorthAccountTypeBreakdownDto> ByAccountType { get; init; }

    public required IReadOnlyList<NetWorthTopHoldingDto> TopHoldings { get; init; }
}

public sealed class NetWorthCustomerBreakdownDto
{
    public required Guid CustomerId { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<NetWorthCurrencyDto> Currencies { get; init; }
}

public sealed class NetWorthAccountTypeBreakdownDto
{
    public required string Type { get; init; }

    public required IReadOnlyList<NetWorthCurrencyDto> Currencies { get; init; }
}

public sealed class NetWorthTopHoldingDto
{
    public required Guid InstrumentId { get; init; }

    public required string Symbol { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal MarketValue { get; init; }

    public required string Currency { get; init; }
}
