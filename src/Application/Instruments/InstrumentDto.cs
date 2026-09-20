using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Instruments;

public sealed class InstrumentDto
{
    public required Guid Id { get; init; }

    public required Guid TenantId { get; init; }

    public required string Symbol { get; init; }

    public required string Name { get; init; }

    public required string QuoteCurrency { get; init; }

    public required bool IsActive { get; init; }

    public required string RowVersion { get; init; }

    public static InstrumentDto From(Instrument instrument, Tenant tenant) => new()
    {
        Id = instrument.PublicId,
        TenantId = tenant.PublicId,
        Symbol = instrument.Symbol,
        Name = instrument.Name,
        QuoteCurrency = instrument.QuoteCurrency,
        IsActive = instrument.IsActive,
        RowVersion = Convert.ToBase64String(instrument.RowVersion ?? [])
    };
}
