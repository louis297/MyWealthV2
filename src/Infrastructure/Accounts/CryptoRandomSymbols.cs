using System.Security.Cryptography;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Infrastructure.Accounts;

/// <summary>Uniform random integers from <see cref="RandomNumberGenerator"/>.</summary>
public sealed class CryptoRandomSymbols : IRandomSymbols
{
    public static CryptoRandomSymbols Instance { get; } = new();

    public int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
}
