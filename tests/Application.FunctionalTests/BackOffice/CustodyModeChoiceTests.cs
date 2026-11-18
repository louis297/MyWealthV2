extern alias BackOfficeHost;

using BackOfficeHost::MyWealthV2.BackOffice;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

// back-office BO14, BO15: the custody mode boxes on the tenant pages. The page tests only see the HTML the
// server renders, so what happens when a box is unchecked is proved on the form model here.
public class CustodyModeChoiceTests
{
    [Test]
    public void Both_ChecksBothBoxes_AndSendsBothInOrder()
    {
        var choice = CustodyModeChoice.Both();

        choice.AllowsExternal.ShouldBeTrue();
        choice.AllowsCustodial.ShouldBeTrue();
        choice.CanSubmit.ShouldBeTrue();
        choice.ToList().ShouldBe(["External", "Custodial"]);
    }

    [Test]
    public void NoBoxChecked_CannotBeSubmitted()
    {
        var choice = CustodyModeChoice.Both();
        choice.AllowsExternal = false;
        choice.AllowsCustodial = false;

        choice.CanSubmit.ShouldBeFalse();
    }

    [TestCase(true, false, "External")]
    [TestCase(false, true, "Custodial")]
    public void OneBoxChecked_SendsExactlyThatValue(bool external, bool custodial, string expected)
    {
        var choice = new CustodyModeChoice { AllowsExternal = external, AllowsCustodial = custodial };

        choice.CanSubmit.ShouldBeTrue();
        choice.ToList().ShouldBe([expected]);
    }

    [Test]
    public void From_SetsTheBoxesFromTheItem()
    {
        var choice = CustodyModeChoice.From(["Custodial"]);

        choice.AllowsExternal.ShouldBeFalse();
        choice.AllowsCustodial.ShouldBeTrue();
        choice.ToList().ShouldBe(["Custodial"]);
    }
}
