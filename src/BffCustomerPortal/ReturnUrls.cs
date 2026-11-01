namespace MyWealthV2.BffCustomerPortal;

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

        return !returnUrl.Contains('\\');
    }
}
