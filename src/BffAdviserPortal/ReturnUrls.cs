namespace MyWealthV2.BffAdviserPortal;

public static class ReturnUrls
{
    public static bool IsAllowed(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return true;
        }

        if (!returnUrl.StartsWith('/'))
        {
            return false;
        }

        if (returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return false;
        }

        // Printable ASCII only. Browsers drop a tab or line break from a URL, so "/\t/host"
        // would become "//host"; a character outside ASCII cannot go into the Location header.
        foreach (var c in returnUrl)
        {
            if (c == '\\' || c < ' ' || c > '~')
            {
                return false;
            }
        }

        return true;
    }
}
