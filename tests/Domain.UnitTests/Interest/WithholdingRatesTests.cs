using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Interest;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Interest;

/// <summary>Interest-run R2–R4: the tax profile and the withholding it gives.</summary>
public class WithholdingRatesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 22, 3, 0, 0, TimeSpan.Zero);

    [TestCase(10.5)]
    [TestCase(17.5)]
    [TestCase(30)]
    [TestCase(33)]
    [TestCase(39)]
    public void NewZealand_IrdHeld_ChosenRate_IsRwtAtThatRate_Elected(decimal rate)
    {
        WithholdingRates.For(TaxResidency.NewZealand, true, rate)
            .ShouldBe(new Withholding(TaxKind.Rwt, rate, WithholdingBasis.Elected));
    }

    [Test]
    public void NewZealand_IrdHeld_NoRate_IsRwt33_Default()
    {
        WithholdingRates.For(TaxResidency.NewZealand, true, null)
            .ShouldBe(new Withholding(TaxKind.Rwt, 33m, WithholdingBasis.Default));
    }

    [Test]
    public void NewZealand_IrdNotHeld_IsRwt45_NonDeclaration()
    {
        WithholdingRates.For(TaxResidency.NewZealand, false, null)
            .ShouldBe(new Withholding(TaxKind.Rwt, 45m, WithholdingBasis.NonDeclaration));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Overseas_IsNrwt15_NonResident(bool irdNumberHeld)
    {
        WithholdingRates.For(TaxResidency.Overseas, irdNumberHeld, null)
            .ShouldBe(new Withholding(TaxKind.Nrwt, 15m, WithholdingBasis.NonResident));
    }

    [TestCase(10.5, true)]
    [TestCase(39, true)]
    [TestCase(45, false)]
    [TestCase(15, false)]
    [TestCase(0, false)]
    [TestCase(17.50, true)]
    public void IsElective_IsTheFiveRates(decimal rate, bool expected)
    {
        WithholdingRates.IsElective(rate).ShouldBe(expected);
    }

    [Test]
    public void NotProvided_IsNewZealand_NotHeld_NoRate_AndRwt45()
    {
        var profile = CustomerTaxProfile.NotProvided;

        profile.TaxResidency.ShouldBe(TaxResidency.NewZealand);
        profile.IrdNumberHeld.ShouldBeFalse();
        profile.RwtRatePercent.ShouldBeNull();
        profile.Withholding.ShouldBe(new Withholding(TaxKind.Rwt, 45m, WithholdingBasis.NonDeclaration));
    }

    [Test]
    public void Create_SetsFields_AndTheWithholding()
    {
        var profile = CustomerTaxProfile.Create(1, 7, TaxResidency.NewZealand, true, 17.5m, 3, At);

        profile.TenantId.ShouldBe(1);
        profile.UserId.ShouldBe(7);
        profile.TaxResidency.ShouldBe(TaxResidency.NewZealand);
        profile.IrdNumberHeld.ShouldBeTrue();
        profile.RwtRatePercent.ShouldBe(17.5m);
        profile.UpdatedByUserId.ShouldBe(3);
        profile.UpdatedAt.ShouldBe(At);
        profile.Withholding.ShouldBe(new Withholding(TaxKind.Rwt, 17.5m, WithholdingBasis.Elected));
    }

    [Test]
    public void Set_ReplacesTheAttributes_AndWhoChangedThem()
    {
        var profile = CustomerTaxProfile.Create(1, 7, TaxResidency.NewZealand, true, 17.5m, 3, At);

        profile.Set(TaxResidency.Overseas, false, null, 4, At.AddDays(1));

        profile.TaxResidency.ShouldBe(TaxResidency.Overseas);
        profile.IrdNumberHeld.ShouldBeFalse();
        profile.RwtRatePercent.ShouldBeNull();
        profile.UpdatedByUserId.ShouldBe(4);
        profile.UpdatedAt.ShouldBe(At.AddDays(1));
        profile.Withholding.Kind.ShouldBe(TaxKind.Nrwt);
    }

    [Test]
    public void Overseas_StoresIrdNumberHeld_AsSent()
    {
        CustomerTaxProfile.Create(1, 7, TaxResidency.Overseas, true, null, 3, At).IrdNumberHeld.ShouldBeTrue();
    }

    [TestCase(TaxResidency.NewZealand, false, 17.5, "A rate can be chosen only when the IRD number is held.")]
    [TestCase(TaxResidency.Overseas, true, 17.5, "An overseas customer has no RWT rate.")]
    [TestCase(TaxResidency.Overseas, false, 17.5, "An overseas customer has no RWT rate.")]
    [TestCase(TaxResidency.NewZealand, true, 45, "RWT rate must be 10.5, 17.5, 30, 33 or 39.")]
    [TestCase(TaxResidency.NewZealand, true, 0, "RWT rate must be 10.5, 17.5, 30, 33 or 39.")]
    public void ARefusedShape_Throws_OnCreateAndSet(TaxResidency residency, bool irdNumberHeld, decimal rate, string message)
    {
        Should.Throw<DomainException>(() => CustomerTaxProfile.Create(1, 7, residency, irdNumberHeld, rate, 3, At))
            .Message.ShouldBe(message);
        var profile = CustomerTaxProfile.Create(1, 7, TaxResidency.NewZealand, false, null, 3, At);
        Should.Throw<DomainException>(() => profile.Set(residency, irdNumberHeld, rate, 3, At)).Message.ShouldBe(message);
        profile.TaxResidency.ShouldBe(TaxResidency.NewZealand);
        profile.IrdNumberHeld.ShouldBeFalse();
    }
}
