namespace MyWealthV2.BackOffice;

/// <summary>The "Broker connection" select on the tenant pages (orders BO26).</summary>
public static class BrokerConnectionChoice
{
    public static IReadOnlyList<string> Options(IReadOnlyList<BrokerConnectionItem> connections, bool create, string? current = null) =>
        throw new NotImplementedException();

    public static string Label(string? value) => throw new NotImplementedException();

    public static string? ToBody(string? value) => throw new NotImplementedException();
}
