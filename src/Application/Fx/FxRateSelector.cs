using MyWealthV2.Application.Common.Exceptions;

namespace MyWealthV2.Application.Fx;

public static class FxRateSelector
{
    public static decimal Select(
        string from,
        string to,
        DateOnly asOf,
        IReadOnlyList<FxRateQuote> rows)
    {
        var source = from.Trim().ToUpperInvariant();
        var target = to.Trim().ToUpperInvariant();
        if (source.Length > 0 && source == target)
        {
            return 1m;
        }

        FxRateQuote? chosen = null;
        foreach (var row in rows)
        {
            if (!string.Equals(row.BaseCurrency.Trim(), source, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.QuoteCurrency.Trim(), target, StringComparison.OrdinalIgnoreCase)
                || row.AsOfDate > asOf)
            {
                continue;
            }

            if (chosen is null || row.AsOfDate > chosen.Value.AsOfDate)
            {
                chosen = row;
            }
        }

        if (chosen is null)
        {
            throw new FxRateNotFoundException(source, target, asOf);
        }

        return chosen.Value.Rate;
    }
}
