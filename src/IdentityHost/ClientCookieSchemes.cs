using Microsoft.AspNetCore.Authentication.Cookies;

namespace MyWealthV2.IdentityHost;

public static class ClientCookieSchemes
{
    public const string AdviserPortal = "adviser-portal";
    public const string BackOffice = "back-office";
    public const string AdviserPortalCookie = ".AspNetCore.Identity.AdviserPortal";
    public const string BackOfficeCookie = ".AspNetCore.Identity.BackOffice";

    public static bool TryGet(string? clientId, out string scheme)
    {
        if (clientId is AdviserPortal or BackOffice)
        {
            scheme = clientId;
            return true;
        }

        scheme = string.Empty;
        return false;
    }

    public static void Configure(CookieAuthenticationOptions options, string cookieName, bool development)
    {
        options.LoginPath = "/login";
        options.Cookie.Name = cookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.Path = "/";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = development
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    }
}
