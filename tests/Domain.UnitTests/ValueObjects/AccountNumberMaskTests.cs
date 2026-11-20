using MyWealthV2.Domain.ValueObjects;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.ValueObjects;

/// <summary>Deposit matching R11: only the last four letters or digits of the payer's account are kept.</summary>
public class AccountNumberMaskTests
{
    [TestCase("12-3456-7890042-00", "****4200")]
    [TestCase("GB29 NWBK 6016 1331 9268 19", "****6819")]
    [TestCase("ab12c", "****b12c")]
    [TestCase("12345", "****2345")]
    [TestCase("1234", "****")]
    [TestCase("1-2-3", "****")]
    [TestCase("--", "****")]
    public void Mask_KeepsTheLastFourLettersOrDigits(string payerAccount, string expected) =>
        AccountNumberMask.Mask(payerAccount).ShouldBe(expected);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Mask_OfNothing_IsNull(string? payerAccount) => AccountNumberMask.Mask(payerAccount).ShouldBeNull();
}
