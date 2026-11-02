namespace MyWealthV2.Application.Fx;

public static class FxRateSelector
{
    public static decimal Select(
        string from,
        string to,
        DateOnly asOf,
        IReadOnlyList<FxRateQuote> rows)
    {
        var source = from.Trim();
        var target = to.Trim();
        if (source.Length > 0 && string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        throw new NotImplementedException();
    }
}
