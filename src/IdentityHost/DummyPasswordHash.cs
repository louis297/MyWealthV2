using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.IdentityHost;

/// <summary>
/// Hash of a random password, made once by the registered hasher so its cost matches a real hash.
/// Hosted login verifies against it when no identity user is found (identity-auth amendment 2026-11-13, lockout).
/// </summary>
public sealed class DummyPasswordHash
{
    public DummyPasswordHash(IServiceScopeFactory scopes)
    {
        using var scope = scopes.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
        Value = hasher.HashPassword(new ApplicationUser(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public string Value { get; }
}
