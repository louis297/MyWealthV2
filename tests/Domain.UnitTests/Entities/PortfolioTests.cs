using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class PortfolioTests
{
    [Test]
    public void Create_LastLineAbsorbsRoundingSoWeightsSumTo100()
    {
        var portfolio = Portfolio.Create(1, "Balanced", Nzd(),
        [
            (10, 33.33m),
            (11, 33.33m),
            (12, 33.33m)
        ]);

        portfolio.Lines.Select(line => line.TargetWeight).ShouldBe([33.33m, 33.33m, 33.34m]);
        portfolio.Lines.Sum(line => line.TargetWeight).ShouldBe(100.00m);
    }

    [Test]
    public void Create_RejectsWeightsThatAreNotARoundingDifference()
    {
        Should.Throw<DomainException>(() => Portfolio.Create(1, "Balanced", Nzd(),
        [
            (10, 60m),
            (11, 30m)
        ]));
    }

    private static Currency Nzd() => Currency.Create("NZD", "New Zealand Dollar", 2);
}
