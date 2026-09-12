namespace MyWealthV2.IdentityHost;

public static class OpenIddictClientUris
{
    public static readonly Uri FallbackRedirect = new("https://localhost/callback");
    public static readonly Uri FallbackPostLogout = new("https://localhost/");

    public static IReadOnlyList<string> ReadPortalOrigins(IConfiguration configuration)
    {
        var origins = new List<string>();
        var many = configuration.GetSection("Identity:PortalOrigins").Get<string[]>();
        if (many is not null)
        {
            origins.AddRange(many);
        }

        var single = configuration["Identity:PortalOrigin"];
        if (!string.IsNullOrWhiteSpace(single))
        {
            origins.Add(single);
        }

        return origins
            .Select(NormalizeOrigin)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static (IReadOnlyList<Uri> Redirects, IReadOnlyList<Uri> PostLogout) FromOrigins(IEnumerable<string> origins)
    {
        var normalized = origins
            .Select(NormalizeOrigin)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            return ([FallbackRedirect], [FallbackPostLogout]);
        }

        return (
            normalized.Select(origin => new Uri(origin + "/callback")).ToArray(),
            normalized.Select(origin => new Uri(origin + "/")).ToArray());
    }

    private static string? NormalizeOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().TrimEnd('/');
    }
}
