namespace MyWealthV2.Application.Common.Interfaces;

public interface IFxRate
{
    Task<decimal> GetRateAsync(
        string from,
        string to,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored row for the directed pair with the latest <c>AsOfDate</c> on or before the date of
    /// <paramref name="asOf"/>, with its as-of date and source. The inverse pair is not used.
    /// Throws <see cref="Exceptions.FxRateNotFoundException"/> when there is none.
    /// </summary>
    Task<FxRateReading> GetRateDetailAsync(
        string from,
        string to,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);
}

/// <summary>One stored rate: quote currency per 1 base currency.</summary>
public sealed record FxRateReading(decimal Rate, DateOnly AsOfDate, string Source);
