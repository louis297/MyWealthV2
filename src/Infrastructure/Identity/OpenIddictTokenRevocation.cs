using MyWealthV2.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace MyWealthV2.Infrastructure.Identity;

public sealed class OpenIddictTokenRevocation(IServiceScopeFactory scopes) : ITokenRevocation
{
    public async Task RevokeSubject(string subject, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        await tokens.RevokeBySubjectAsync(subject, cancellationToken);
    }
}
