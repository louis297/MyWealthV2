namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>
/// FX conversions R12: the stored rate is not the rate the caller confirmed (api-design §4.2 <c>rate_changed</c>).
/// The message also goes in <c>errors.expectedRate</c>.
/// </summary>
public sealed class ExpectedRateChangedException(decimal currentRate)
    : ProblemException(
        ProblemKind.Conflict,
        "rate_changed",
        "The rate changed",
        message: RateChangedMessage)
{
    private const string RateChangedMessage = "The rate changed since the quote. Get a new quote and confirm again.";

    public decimal CurrentRate { get; } = currentRate;

    public override IReadOnlyDictionary<string, string[]> Errors => new Dictionary<string, string[]>
    {
        ["ExpectedRate"] = [$"{RateChangedMessage} Current rate: {CurrentRate}."]
    };
}
