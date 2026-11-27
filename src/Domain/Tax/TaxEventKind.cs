namespace MyWealthV2.Domain.Tax;

/// <summary>What happened to a tax holding, as the tax rules see it (tax-year-events R9).</summary>
public enum TaxEventKind
{
    Acquisition = 0,
    Disposal = 1,
    Dividend = 2,
    Reorganisation = 3,
    Arrival = 4,
    Departure = 5,
    Move = 6,
    Correction = 7
}
