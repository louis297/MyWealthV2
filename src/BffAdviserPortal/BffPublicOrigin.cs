namespace MyWealthV2.BffAdviserPortal;

public static class BffPublicOrigin
{
    public const string ConfigurationKey = "Authentication:PublicOrigin";

    public static string? Read(IConfiguration configuration)
    {
        var value = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().TrimEnd('/');
    }
}
