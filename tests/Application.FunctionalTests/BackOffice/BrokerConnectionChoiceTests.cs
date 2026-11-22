extern alias BackOfficeHost;

using BackOfficeHost::MyWealthV2.BackOffice;

namespace MyWealthV2.Application.FunctionalTests.BackOffice;

/// <summary>Orders BO26: the "Broker connection" select of the tenant forms.</summary>
public class BrokerConnectionChoiceTests
{
    private static readonly IReadOnlyList<BrokerConnectionItem> Connections =
    [
        new("simulator", "Simulator", null),
        new("other-broker", "Simulator", null)
    ];

    [Test]
    public void OnCreate_OffersNoneFirst_ThenTheConfiguredConnections() =>
        BrokerConnectionChoice.Options(Connections, create: true).ShouldBe(["", "simulator", "other-broker"]);

    [Test]
    public void OnEdit_OffersNoNone_AndKeepsTheTenantsOwnConnectionWhenItIsNoLongerConfigured()
    {
        BrokerConnectionChoice.Options(Connections, create: false, current: "simulator").ShouldBe(["simulator", "other-broker"]);
        BrokerConnectionChoice.Options(Connections, create: false, current: null).ShouldBe(["simulator", "other-broker"]);
        BrokerConnectionChoice.Options(Connections, create: false, current: "retired").ShouldBe(["simulator", "other-broker", "retired"]);
    }

    [Test]
    public void NoneIsLabelledNone_AndSendsNoConnection()
    {
        (BrokerConnectionChoice.Label(""), BrokerConnectionChoice.Label(null), BrokerConnectionChoice.Label("simulator"))
            .ShouldBe(("None", "None", "simulator"));
        (BrokerConnectionChoice.ToBody(""), BrokerConnectionChoice.ToBody(null), BrokerConnectionChoice.ToBody("simulator"))
            .ShouldBe(((string?)null, (string?)null, (string?)"simulator"));
    }
}
