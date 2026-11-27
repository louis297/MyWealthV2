namespace MyWealthV2.Domain.Valuations;

/// <summary>
/// The position of a day (docs/features/account-valuation.md R3, R4): the cash and the ledger quantity of each
/// Instrument at the end of <see cref="Day"/>, from the legs booked before <see cref="LedgerDay.End"/>. An Instrument
/// whose quantity is 0 has no line. <see cref="CutAt"/> is the instant the position was cut at, which R18 compares the
/// <c>bookedAt</c> of an entitlement posting with: the end of the day for a position of a day, the start or end of a
/// tax year for the tax year read (docs/features/tax-year-events.md R22).
/// </summary>
public sealed record AccountPosition(DateOnly Day, decimal Cash, IReadOnlyList<PositionLine> Lines, DateTimeOffset CutAt)
{
    /// <summary>A position of a day: cut at <see cref="LedgerDay.End"/> of <paramref name="day"/>.</summary>
    public AccountPosition(DateOnly day, decimal cash, IReadOnlyList<PositionLine> lines)
        : this(day, cash, lines, LedgerDay.End(day))
    {
    }
}

/// <summary>One Instrument of a position with the sum of its security legs: the ledger quantity.</summary>
public sealed record PositionLine(int InstrumentId, decimal LedgerQuantity);
