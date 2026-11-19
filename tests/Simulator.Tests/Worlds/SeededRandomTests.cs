using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Tests.Worlds;

/// <summary>Simulator spec R26: every random choice comes from (seed, purpose, sequence).</summary>
public class SeededRandomTests
{
    [Test]
    public void SameSeedPurposeAndSequence_GiveTheSameDraws()
    {
        var first = Draws(SeededRandom.For(42, "bank.statement", 3));
        var second = Draws(SeededRandom.For(42, "bank.statement", 3));

        second.ShouldBe(first);
    }

    [TestCase(43, "bank.statement", 3)]
    [TestCase(42, "broker.fill", 3)]
    [TestCase(42, "bank.statement", 4)]
    public void AnyDifferentInput_GivesDifferentDraws(int seed, string purpose, long sequence)
    {
        var baseline = Draws(SeededRandom.For(42, "bank.statement", 3));

        Draws(SeededRandom.For(seed, purpose, sequence)).ShouldNotBe(baseline);
    }

    // The derivation is fixed: SHA-256 of "{seed}|{purpose}|{sequence}" in UTF-8, first four bytes as a
    // little-endian int. If it changed, every stored seed would replay differently.
    [TestCase(1, "test", 0, -1959316328)]
    [TestCase(42, "bank.statement", 3, 1147433824)]
    public void DerivedSeed_IsFixed(int seed, string purpose, long sequence, int expected)
    {
        SeededRandom.DerivedSeed(seed, purpose, sequence).ShouldBe(expected);
    }

    private static int[] Draws(Random random) => Enumerable.Range(0, 8).Select(_ => random.Next()).ToArray();
}
