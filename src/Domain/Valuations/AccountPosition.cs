namespace MyWealthV2.Domain.Valuations;

/// <summary>
/// The position of a day (docs/features/account-valuation.md R3, R4): the cash and the ledger quantity of each
/// Instrument at the end of <see cref="Day"/>, from the legs booked before <see cref="LedgerDay.End"/>. An Instrument
/// whose quantity is 0 has no line.
/// </summary>
public sealed record AccountPosition(DateOnly Day, decimal Cash, IReadOnlyList<PositionLine> Lines);

/// <summary>One Instrument of a position with the sum of its security legs: the ledger quantity.</summary>
public sealed record PositionLine(int InstrumentId, decimal LedgerQuantity);
