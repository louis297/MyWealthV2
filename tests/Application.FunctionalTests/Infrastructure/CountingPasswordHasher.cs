using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

/// <summary>Counts password-hash verifications on the identity host (identity-auth amendment A41).</summary>
public sealed class PasswordVerificationCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Reset() => Interlocked.Exchange(ref _count, 0);

    internal void Increment() => Interlocked.Increment(ref _count);
}

public sealed class CountingPasswordHasher(
    IOptions<PasswordHasherOptions> options,
    PasswordVerificationCounter counter) : PasswordHasher<ApplicationUser>(options)
{
    public override PasswordVerificationResult VerifyHashedPassword(
        ApplicationUser user,
        string hashedPassword,
        string providedPassword)
    {
        counter.Increment();
        return base.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}
