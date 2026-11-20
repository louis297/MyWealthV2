using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.ValueObjects;

/// <summary>Deposit matching R15: deposit reference candidates from a bank reference text.</summary>
public class DepositReferenceScannerTests
{
    [TestCase("K7M3Q9PXB")]
    [TestCase("k7m-3q9-pxb")]
    [TestCase("K7M 3Q9 PXB")]
    [TestCase("Dep K7M3Q9PXB")]
    [TestCase("K7M3Q9PXB / rent")]
    [TestCase("  K7M3Q9PXB  K7M3Q9PXB ")]
    public void OneValidReference_GivesOneCandidate(string text) =>
        Values(text).ShouldBe(["K7M3Q9PXB"]);

    [TestCase("K1MOQ9PXX")]
    [TestCase("KLM0Q9PXX")]
    [TestCase("KIM0Q9PXX")]
    [TestCase("klm-oq9-pxx")]
    public void OIAndL_ReadAsZeroAndOne(string text) =>
        Values(text).ShouldBe(["K1M0Q9PXX"]);

    [TestCase("K7M3Q9PXC")]
    [TestCase("K7M3Q9PBX")]
    [TestCase("X7M3Q9PXB")]
    public void AMistypedCharacter_FailsTheCheck_AndGivesNothing(string text) =>
        Values(text).ShouldBeEmpty();

    [Test]
    public void TwoValidReferences_GiveTwoCandidates() =>
        Values("K7M3Q9PXB and H2N4R6T80").ShouldBe(["K7M3Q9PXB", "H2N4R6T80"]);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Rent for November")]
    public void NoReference_GivesNothing(string? text) => Values(text).ShouldBeEmpty();

    [TestCase("K7M3Q9PXBA")]
    [TestCase("AK7M3Q9PXB")]
    public void ATenCharacterToken_GivesNothing(string text) => Values(text).ShouldBeEmpty();

    [Test]
    public void NonAsciiLetters_SeparateTokens() =>
        Values("Zahlung K7M3Q9PXB für Miete").ShouldBe(["K7M3Q9PXB"]);

    private static string[] Values(string? text) =>
        DepositReferenceScanner.Candidates(text).Select(reference => reference.Value).ToArray();
}
