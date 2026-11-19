namespace MyWealthV2.Domain.ValueObjects;

/// <summary>A source of uniformly random integers, for <see cref="DepositReference.Generate"/>.</summary>
public interface IRandomSymbols
{
    /// <summary>A uniformly random integer in <c>[0, maxExclusive)</c>.</summary>
    int Next(int maxExclusive);
}
