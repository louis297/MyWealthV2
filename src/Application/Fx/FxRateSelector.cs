namespace MyWealthV2.Application.Fx;

public static class FxRateSelector
{
    public static decimal Select(
        string from,
        string to,
        DateOnly asOf,
        IReadOnlyList<FxRateQuote> rows)
    {
        throw new NotImplementedException();
    }
}
