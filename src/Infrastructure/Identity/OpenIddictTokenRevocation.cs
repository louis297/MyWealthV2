using MyWealthV2.Application.Common.Interfaces;
using OpenIddict.Abstractions;

namespace MyWealthV2.Infrastructure.Identity;

public sealed class OpenIddictTokenRevocation(IOpenIddictTokenManager tokens) : ITokenRevocation
{
    public async Task RevokeSubject(string subject, CancellationToken cancellationToken)
    {
        await foreach (var token in tokens.FindBySubjectAsync(subject, cancellationToken))
        {
            await tokens.TryRevokeAsync(token, cancellationToken);
        }
    }
}
