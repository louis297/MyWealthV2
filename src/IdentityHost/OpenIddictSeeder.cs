using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MyWealthV2.IdentityHost;

public static class OpenIddictSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        if (await manager.FindByClientIdAsync("adviser-portal", cancellationToken) is not null)
        {
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = "adviser-portal",
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Adviser Portal",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + "api"
            },
            Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange
            }
        };

        var redirects = services.GetRequiredService<IConfiguration>()
            .GetSection("Identity:RedirectUris")
            .Get<string[]>() ?? [];

        foreach (var uri in redirects)
        {
            descriptor.RedirectUris.Add(new Uri(uri));
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
        }

        if (descriptor.RedirectUris.Count == 0)
        {
            descriptor.RedirectUris.Add(new Uri("https://localhost/callback"));
            descriptor.PostLogoutRedirectUris.Add(new Uri("https://localhost/"));
        }

        await manager.CreateAsync(descriptor, cancellationToken);
    }
}
