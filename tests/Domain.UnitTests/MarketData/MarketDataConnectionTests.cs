using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.MarketData;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.MarketData;

/// <summary>Simulated instruments R2, R4: a connection serves the symbols that end with its suffix.</summary>
public class MarketDataConnectionTests
{
    private static readonly MarketDataConnection Simulator = new("simulator", "Simulator", ".SIM");

    [TestCase("ACME.SIM")]
    [TestCase("acme.sim")]
    [TestCase("KIWI.Sim")]
    [TestCase("A-B.C.SIM")]
    public void Serves_ASymbolEndingWithTheSuffix(string symbol)
    {
        Simulator.Serves(symbol).ShouldBeTrue();
    }

    [TestCase("ACME")]
    [TestCase("SIM")]
    [TestCase(".SIM")]
    [TestCase("ACME.SIMX")]
    [TestCase("ACMESIM")]
    [TestCase("")]
    [TestCase(null)]
    public void DoesNotServe_AnyOtherSymbol(string? symbol)
    {
        Simulator.Serves(symbol).ShouldBeFalse();
    }

    [TestCase(".SIM")]
    [TestCase(".sim")]
    [TestCase(".X1")]
    [TestCase(".ABCDEFGHIJ")]
    public void IsValidSuffix_ADotAndTwoToTenLettersOrDigits(string suffix)
    {
        MarketDataConnection.IsValidSuffix(suffix).ShouldBeTrue();
    }

    [TestCase("SIM")]
    [TestCase(".S")]
    [TestCase(".ABCDEFGHIJK")]
    [TestCase("..SIM")]
    [TestCase(".SI-M")]
    [TestCase(".SIM ")]
    [TestCase("")]
    [TestCase(null)]
    public void IsValidSuffix_RefusesAnyOtherShape(string? suffix)
    {
        MarketDataConnection.IsValidSuffix(suffix).ShouldBeFalse();
    }

    [Test]
    public void For_ReturnsTheOneConnectionThatServesTheSymbol_OrNone()
    {
        var other = new MarketDataConnection("other", "Simulator", ".OTH");
        var connections = new MarketDataConnections([other, Simulator]);

        connections.For("acme.sim").ShouldBe(Simulator);
        connections.For("FOO.OTH").ShouldBe(other);
        connections.For("AAPL.US").ShouldBeNull();
        connections.For(null).ShouldBeNull();
        connections.All.Select(connection => connection.Name).ShouldBe(["other", "simulator"]);
    }

    [Test]
    public void Named_FindsAConnectionByItsName()
    {
        var connections = new MarketDataConnections([Simulator]);

        connections.Named("simulator").ShouldBe(Simulator);
        connections.Named("other").ShouldBeNull();
        connections.Named(null).ShouldBeNull();
    }

    [Test]
    public void TwoConnectionsWithTheSameSuffix_AreRefused_WithoutRegardToCase()
    {
        Should.Throw<DomainException>(() => new MarketDataConnections(
            [Simulator, new MarketDataConnection("second", "Simulator", ".sim")]));
    }

    [Test]
    public void None_ServesNothing()
    {
        MarketDataConnections.None.All.ShouldBeEmpty();
        MarketDataConnections.None.For("ACME.SIM").ShouldBeNull();
    }
}
