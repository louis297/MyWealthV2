using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace MyWealthV2.IdentityHost;

public sealed class RedirectUriMismatchLogger(
    IOpenIddictApplicationManager applications,
    ILogger<RedirectUriMismatchLogger> logger)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateAuthorizationRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; }
        = OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ValidateAuthorizationRequestContext>()
            .UseScopedHandler<RedirectUriMismatchLogger>()
            .SetOrder(OpenIddictServerHandlers.Authentication.ValidateClientRedirectUri.Descriptor.Order - 1)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(OpenIddictServerEvents.ValidateAuthorizationRequestContext context)
    {
        var challenge = context.RedirectUri;
        if (string.IsNullOrEmpty(context.ClientId) || string.IsNullOrEmpty(challenge))
        {
            return;
        }

        var application = await applications.FindByClientIdAsync(context.ClientId, context.CancellationToken);
        if (application is null)
        {
            return;
        }

        var registered = await applications.GetRedirectUrisAsync(application, context.CancellationToken);
        foreach (var uri in registered)
        {
            if (string.Equals(uri, challenge, StringComparison.Ordinal))
            {
                return;
            }
        }

        logger.LogWarning(
            "OIDC challenge redirect_uri {RedirectUri} differs from registered redirects {RegisteredRedirects}.",
            challenge,
            string.Join(", ", registered));
    }
}
