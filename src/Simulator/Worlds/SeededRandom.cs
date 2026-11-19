namespace MyWealthV2.Simulator.Worlds;

/// <summary>Simulator spec R26. Stub.</summary>
public static class SeededRandom
{
    public static Random For(int seed, string purpose, long sequence) => new(0);

    public static int DerivedSeed(int seed, string purpose, long sequence) => 0;
}
