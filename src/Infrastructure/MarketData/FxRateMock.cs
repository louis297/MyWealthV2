using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Infrastructure.MarketData;

public sealed class FxRateMock : IFxRate
{
    private readonly Dictionary<string, decimal> _rates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public void SetRate(string from, string to, decimal rate)
    {
        lock (_gate)
        {
            _rates[Key(from, to)] = rate;
        }
    }

    public decimal GetRate(string from, string to, DateTimeOffset? asOf = null)
    {
        var source = from?.Trim() ?? string.Empty;
        var target = to?.Trim() ?? string.Empty;
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && source.Length > 0)
        {
            return 1m;
        }

        lock (_gate)
        {
            if (_rates.TryGetValue(Key(source, target), out var rate))
            {
                return rate;
            }
        }

        throw new InvalidOperationException($"No FX rate for {source}/{target}.");
    }

    private static string Key(string from, string to) =>
        $"{from.Trim().ToUpperInvariant()}|{to.Trim().ToUpperInvariant()}";
}
