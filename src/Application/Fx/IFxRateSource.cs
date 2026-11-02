namespace MyWealthV2.Application.Fx;

public interface IFxRateSource
{
    Task<IReadOnlyList<FxRateQuote>> GetLatestAsync(
        string baseCurrency,
        IReadOnlyList<string> quotes,
        CancellationToken cancellationToken = default);
}
