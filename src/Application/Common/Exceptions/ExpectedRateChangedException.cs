namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>FX conversions R12: the stored rate is not the rate the caller confirmed. Maps to 409.</summary>
public sealed class ExpectedRateChangedException(decimal currentRate)
    : Exception("The rate changed since the quote. Get a new quote and confirm again.")
{
    public decimal CurrentRate { get; } = currentRate;
}
