using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Accounts;

public sealed class AccountHoldingDto
{
    public required Guid Id { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid InstrumentId { get; init; }
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required Decimal Quantity { get; init; }
    public required Decimal Cost { get; init; }
    public required string CostCurrency { get; init; }

    public static AccountHoldingDto From(Account account, Holding holding, Instrument instrument) => new()
    {
        Id = holding.PublicId,
        AccountId = account.PublicId,
        InstrumentId = instrument.PublicId,
        Symbol = instrument.Symbol,
        Name = instrument.Name,
        Quantity = holding.Quantity,
        Cost = holding.CostAmount,
        CostCurrency = holding.CostCurrency,
    };
}
