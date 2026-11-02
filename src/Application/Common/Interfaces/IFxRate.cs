namespace MyWealthV2.Application.Common.Interfaces;

public interface IFxRate
{
    Task<decimal> GetRateAsync(
        string from,
        string to,
        DateTimeOffset? asOf = null,
        CancellationToken cancellationToken = default);
}
