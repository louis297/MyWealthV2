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

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var origins = OpenIddictClientUris.ReadPortalOrigins(configuration);
        var (redirects, postLogout) = OpenIddictClientUris.FromOrigins(origins);

        var existing = await manager.FindByClientIdAsync("adviser-portal", cancellationToken);
        var descriptor = new OpenIddictApplicationDescriptor();
        if (existing is not null)
        {
            await manager.PopulateAsync(descriptor, existing, cancellationToken);
        }

        descriptor.ClientId = "adviser-portal";
        descriptor.ClientType = ClientTypes.Public;
        descriptor.ConsentType = ConsentTypes.Implicit;
        descriptor.DisplayName = "Adviser Portal";
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);

        foreach (var permission in RequiredPermissions)
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
