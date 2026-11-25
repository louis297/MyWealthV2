namespace MyWealthV2.Application.Fx;

public interface IFxRateSource
{
    Task<IReadOnlyList<FxRateQuote>> GetLatestAsync(
        string baseCurrency,
        IReadOnlyList<string> quotes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every record the source holds for <paramref name="baseCurrency"/> against <paramref name="quotes"/> from
    /// <paramref name="from"/> to <paramref name="to"/>, both inclusive (docs/features/market-data-history.md R13).
    /// Throws when the call fails.
    /// </summary>
    Task<IReadOnlyList<FxRateQuote>> GetRangeAsync(
        string baseCurrency,
        IReadOnlyList<string> quotes,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
