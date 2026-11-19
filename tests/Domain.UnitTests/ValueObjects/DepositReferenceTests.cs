using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.ValueObjects;

/// <summary>Deposit reference R15 (docs/features/client-money-accounts.md §5).</summary>
public class DepositReferenceTests
{
    [Test]
    public void Generate_WithScriptedSymbols_GivesTheExpectedValue()
    {
        // K is letter 9 of the 22 letters (alphabet index 19); then 7, M (20), 3, Q (23), 9, P (22), X (29).
        var random = new ScriptedSymbols(9, 7, 20, 3, 23, 9, 22, 29);

        var reference = DepositReference.Generate(random);

        reference.Value.ShouldBe("K7M3Q9PXB");
        random.MaxValues.ShouldBe([22, 32, 32, 32, 32, 32, 32, 32]);
    }

    [Test]
    public void Generate_AThousandValues_AreValid_AndStartWithALetter()
    {
        var random = new SeededSymbols(20261120);

        for (var i = 0; i < 1000; i++)
        {
            var value = DepositReference.Generate(random).Value;

            value.Length.ShouldBe(9);
            char.IsAsciiLetterUpper(value[0]).ShouldBeTrue(value);
            DepositReference.IsValid(value).ShouldBeTrue(value);
        }
    }

    [Test]
    public void IsValid_TheSpecExample_IsTrue() =>
        DepositReference.IsValid("K7M3Q9PXB").ShouldBeTrue();

    [TestCase("K7MAQ9PXB")]
    [TestCase("K7M3Q9PXC")]
    [TestCase("A7M3Q9PXB")]
    public void IsValid_OneChangedCharacter_IsFalse(string value) =>
        DepositReference.IsValid(value).ShouldBeFalse();

    [TestCase("K73MQ9PXB")]
    [TestCase("7KM3Q9PXB")]
    [TestCase("K7M3Q9XPB")]
    public void IsValid_TwoNeighboursSwapped_IsFalse(string value) =>
        DepositReference.IsValid(value).ShouldBeFalse();

    [TestCase("K7M3Q9PX")]
    [TestCase("K7M3Q9PXB0")]
    [TestCase("")]
    [TestCase(null)]
    public void IsValid_WrongLength_IsFalse(string? value) =>
        DepositReference.IsValid(value).ShouldBeFalse();

    [TestCase('I')]
    [TestCase('L')]
    [TestCase('O')]
    [TestCase('U')]
    public void IsValid_ALetterOutsideTheAlphabet_IsFalse(char letter)
    {
        // Every position, so no lucky check sum hides it.
        for (var position = 0; position < 9; position++)
        {
            var chars = "K7M3Q9PXB".ToCharArray();
            chars[position] = letter;

            DepositReference.IsValid(new string(chars)).ShouldBeFalse(new string(chars));
        }
    }

    [Test]
    public void IsValid_LowerCase_IsFalse() =>
        DepositReference.IsValid("k7m3q9pxb").ShouldBeFalse();

    [Test]
    public void IsValid_AFirstDigit_IsFalse()
    {
        // 77M3Q9PX with its own valid check symbol: the check passes, the first character does not.
        var withCheck = "77M3Q9PX" + CheckSymbol("77M3Q9PX");

        DepositReference.IsValid(withCheck).ShouldBeFalse();
    }

    [Test]
    public void TryParse_AValidValue_GivesItBack()
    {
        DepositReference.TryParse("K7M3Q9PXB", out var reference).ShouldBeTrue();

        reference.Value.ShouldBe("K7M3Q9PXB");
        reference.ShouldBe(DepositReference.TryParse("K7M3Q9PXB", out var same) ? same : null);
    }

    [Test]
    public void TryParse_AnInvalidValue_IsFalse() =>
        DepositReference.TryParse("K7MAQ9PXB", out _).ShouldBeFalse();

    /// <summary>The Luhn mod 32 check symbol of spec §5, written out independently of the code under test.</summary>
    private static char CheckSymbol(string eight)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var factor = 2;
        var sum = 0;
        for (var i = eight.Length - 1; i >= 0; i--)
        {
            var addend = factor * alphabet.IndexOf(eight[i]);
            sum += addend / 32 + addend % 32;
            factor = factor == 2 ? 1 : 2;
        }

        return alphabet[(32 - sum % 32) % 32];
    }

    private sealed class ScriptedSymbols(params int[] values) : IRandomSymbols
    {
        private int _next;

        public List<int> MaxValues { get; } = [];

        public int Next(int maxExclusive)
        {
            MaxValues.Add(maxExclusive);
            return values[_next++];
        }
    }

    private sealed class SeededSymbols(int seed) : IRandomSymbols
    {
        private readonly Random _random = new(seed);

        public int Next(int maxExclusive) => _random.Next(maxExclusive);
    }
}
