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
        var portalOrigins = OpenIddictClientUris.ReadPortalOrigins(configuration);
        var (portalRedirects, portalPostLogout) = OpenIddictClientUris.FromOrigins(portalOrigins);

        var adviserSecret = configuration["Identity:AdviserPortalClientSecret"];
        if (string.IsNullOrWhiteSpace(adviserSecret))
        {
            throw new InvalidOperationException("Identity:AdviserPortalClientSecret is required.");
        }

        await UpsertAsync(
            manager,
            clientId: "adviser-portal",
            secret: adviserSecret,
            displayName: "Adviser Portal",
            redirects: portalRedirects,
            postLogout: portalPostLogout,
            cancellationToken);

        var backOfficeSecret = configuration["Identity:BackOfficeClientSecret"];
        if (string.IsNullOrWhiteSpace(backOfficeSecret))
        {
            throw new InvalidOperationException("Identity:BackOfficeClientSecret is required.");
        }

        var backOfficeOrigin = OpenIddictClientUris.ReadBackOfficeOrigin(configuration);
        var (backOfficeRedirects, backOfficePostLogout) = OpenIddictClientUris.FromOrigins([backOfficeOrigin]);
        await UpsertAsync(
            manager,
            clientId: "back-office",
            secret: backOfficeSecret,
            displayName: "Back Office",
            redirects: backOfficeRedirects,
            postLogout: backOfficePostLogout,
            cancellationToken);
    }

    private static async Task UpsertAsync(
        IOpenIddictApplicationManager manager,
        string clientId,
        string secret,
        string displayName,
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
        descriptor.ClientType = ClientTypes.Confidential;
        descriptor.ClientSecret = secret;
        descriptor.ConsentType = ConsentTypes.Implicit;
        descriptor.DisplayName = displayName;
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
