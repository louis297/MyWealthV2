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

        throw new FxRateNotFoundException(source, target, asOf);
    }
}
