namespace MyWealthV2.BackOffice;

/// <summary>
/// The "Broker connection" select on the tenant pages (orders BO26). It offers the configured connections; "None" is
/// offered only when a tenant is created, since an update that names no connection leaves the tenant's as it is
/// (orders R3).
/// </summary>
public static class BrokerConnectionChoice
{
    public const string None = "";
    public const string NoneLabel = "None";

    /// <summary>The values of the select, in the order shown. On the edit form the tenant's own connection is offered
    /// even when it is no longer configured, so the form shows what is stored.</summary>
    public static IReadOnlyList<string> Options(IReadOnlyList<BrokerConnectionItem> connections, bool create, string? current = null)
    {
        var options = new List<string>();
        if (create)
        {
            options.Add(None);
        }

        options.AddRange(connections.Select(connection => connection.Name));
        if (!create && !string.IsNullOrEmpty(current) && !options.Contains(current))
        {
            options.Add(current);
        }

        return options;
    }

    public static string Label(string? value) => string.IsNullOrEmpty(value) ? NoneLabel : value;

    /// <summary>The <c>brokerConnection</c> of the request body: null for "None".</summary>
    public static string? ToBody(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
