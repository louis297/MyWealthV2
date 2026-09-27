using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MyWealthV2.IdentityHost;

public static class OpenIddictSeeder
{
    private static readonly string[] RequiredPermissions =
    [
        Permissions.Endpoints.Authorization,
        Permissions.Endpoints.Token,
        Permissions.Endpoints.Revocation,
        Permissions.Endpoints.EndSession,
        Permissions.GrantTypes.AuthorizationCode,
        Permissions.GrantTypes.RefreshToken,
        Permissions.ResponseTypes.Code,
        Permissions.Scopes.Email,
        Permissions.Scopes.Profile,
        Permissions.Prefixes.Scope + Scopes.OfflineAccess,
        Permissions.Prefixes.Scope + "api"
    ];

    private static readonly string[] AngularPermissions =
    [
        Permissions.Endpoints.Authorization,
        Permissions.Endpoints.Token,
        Permissions.Endpoints.Revocation,
        Permissions.Endpoints.EndSession,
        Permissions.GrantTypes.AuthorizationCode,
        Permissions.GrantTypes.RefreshToken,
        Permissions.ResponseTypes.Code,
        Permissions.Scopes.Profile,
        Permissions.Prefixes.Scope + Scopes.OfflineAccess,
        Permissions.Prefixes.Scope + "api"
    ];

    private static readonly Uri AngularFallbackRedirect = new("https://adviser-portal-angular.invalid/callback");
    private static readonly Uri AngularFallbackPostLogout = new("https://adviser-portal-angular.invalid/");

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var origins = OpenIddictClientUris.ReadPortalOrigins(configuration);
        var (redirects, postLogout) = OpenIddictClientUris.FromOrigins(origins);

        await UpsertAsync(
            manager,
            "adviser-portal",
            "Adviser Portal",
            RequiredPermissions,
            redirects,
            postLogout,
            cancellationToken);

        var angularOrigins = OpenIddictClientUris.ReadAdviserPortalAngularOrigins(configuration);
        IReadOnlyList<Uri> angularRedirects;
        IReadOnlyList<Uri> angularPostLogout;
        if (angularOrigins.Count == 0)
        {
            angularRedirects = [AngularFallbackRedirect];
            angularPostLogout = [AngularFallbackPostLogout];
        }
        else
        {
            (angularRedirects, angularPostLogout) = OpenIddictClientUris.FromOrigins(angularOrigins);
        }

        await UpsertAsync(
            manager,
            "adviser-portal-angular",
            "Adviser Portal Angular",
            AngularPermissions,
            angularRedirects,
            angularPostLogout,
            cancellationToken);
    }

    private static async Task UpsertAsync(
        IOpenIddictApplicationManager manager,
        string clientId,
        string displayName,
        IEnumerable<string> permissions,
        IReadOnlyList<Uri> redirects,
        IReadOnlyList<Uri> postLogout,
        CancellationToken cancellationToken)
    {
        var existing = await manager.FindByClientIdAsync(clientId, cancellationToken);
        var descriptor = new OpenIddictApplicationDescriptor();
        if (existing is not null)
        {
            await manager.PopulateAsync(descriptor, existing, cancellationToken);
        }

        descriptor.ClientId = clientId;
        descriptor.ClientType = ClientTypes.Public;
        descriptor.ConsentType = ConsentTypes.Implicit;
        descriptor.DisplayName = displayName;
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

        foreach (var permission in permissions)
        {
            descriptor.Permissions.Add(permission);
        }

        descriptor.RedirectUris.Clear();
        descriptor.PostLogoutRedirectUris.Clear();
        foreach (var uri in redirects)
        {
            descriptor.RedirectUris.Add(uri);
        }

        foreach (var uri in postLogout)
        {
            descriptor.PostLogoutRedirectUris.Add(uri);
        }

        if (existing is null)
        {
            await manager.CreateAsync(descriptor, cancellationToken);
            return;
        }

        await manager.UpdateAsync(existing, descriptor, cancellationToken);
    }
}
