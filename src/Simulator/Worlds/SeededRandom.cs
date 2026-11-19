using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MyWealthV2.Simulator.Worlds;

/// <summary>
/// Simulator spec R26: every random choice the simulator makes is drawn from a generator built from the world's
/// seed, a purpose name and a sequence number, so the same seed and the same steps give the same signals.
/// Never <c>Random.Shared</c> and never <c>Guid.NewGuid()</c> for an outside value.
/// </summary>
public static class SeededRandom
{
    public static Random For(int seed, string purpose, long sequence) =>
        new(DerivedSeed(seed, purpose, sequence));

    /// <summary>
    /// The first four bytes, little-endian, of SHA-256 over <c>"{seed}|{purpose}|{sequence}"</c> in UTF-8.
    /// Fixed: changing it would replay every stored seed differently.
    /// </summary>
    public static int DerivedSeed(int seed, string purpose, long sequence)
    {
        var input = Encoding.UTF8.GetBytes(FormattableString.Invariant($"{seed}|{purpose}|{sequence}"));
        var hash = SHA256.HashData(input);
        return BinaryPrimitives.ReadInt32LittleEndian(hash);
    }

    /// <summary>A seed for a world reset without one. Not drawn from the world: it starts one.</summary>
    public static int NewSeed() => RandomNumberGenerator.GetInt32(int.MaxValue);
}
