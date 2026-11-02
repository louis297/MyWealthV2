using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Events;
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

    [Test]
    public void Create_RejectsAWeightOutsideZeroTo100()
    {
        Should.Throw<DomainException>(() => Portfolio.Create(1, "Balanced", Nzd(), [(10, 0m), (11, 100m)]));
        Should.Throw<DomainException>(() => Portfolio.Create(1, "Balanced", Nzd(), [(10, 100.01m)]));
        Should.Throw<DomainException>(() => Portfolio.Create(1, "Balanced", Nzd(), [(10, -1m), (11, 101m)]));
    }

    [Test]
    public void Create_RejectsADuplicateInstrument()
    {
        Should.Throw<DomainException>(() => Portfolio.Create(1, "Balanced", Nzd(),
        [
            (10, 60m),
            (10, 40m)
        ]));
    }

    [Test]
    public void Create_RejectsAnEmptyLineList()
    {
        Should.Throw<DomainException>(() => Portfolio.Create(1, "Balanced", Nzd(), []))
            .Message.ShouldBe("A portfolio needs at least one line.");
    }

    [Test]
    public void Create_StoresIdentityAndDefaultSettings()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var portfolio = Portfolio.Create(7, "  Balanced Growth  ", Nzd(), [(10, 100m)], id);

        portfolio.TenantId.ShouldBe(7);
        portfolio.Name.ShouldBe("Balanced Growth");
        portfolio.BaseCurrency.ShouldBe("NZD");
        portfolio.IsActive.ShouldBeTrue();
        portfolio.PublicId.ShouldBe(id);
        portfolio.DriftAbsolute.ShouldBe(1m);
        portfolio.DriftRelative.ShouldBe(5m);
        portfolio.MinimumTrade.ShouldBe(100m);
        portfolio.CashResidualPercent.ShouldBe(1m);
        portfolio.Lines.Single().SortOrder.ShouldBe(0);
    }

    [Test]
    public void Create_RejectsABlankNameOrCurrency()
    {
        Should.Throw<DomainException>(() => Portfolio.Create(1, "  ", Nzd(), [(10, 100m)]));
        Should.Throw<DomainException>(() => Portfolio.Create(1, new string('a', 201), Nzd(), [(10, 100m)]));
        Should.Throw<DomainException>(() => Portfolio.Create(1, "Balanced", null!, [(10, 100m)]));
    }

    [Test]
    public void Create_RaisesPortfolioCreated()
    {
        var portfolio = Portfolio.Create(1, "Balanced", Nzd(), [(10, 100m)]);

        portfolio.DomainEvents.OfType<PortfolioCreated>().ShouldHaveSingleItem();
    }

    [Test]
    public void ReplaceLines_RaisesPortfolioWeightsChanged()
    {
        var portfolio = Portfolio.Create(1, "Balanced", Nzd(), [(10, 60m), (11, 40m)]);
        portfolio.ClearDomainEvents();

        portfolio.ReplaceLines([(10, 70m), (11, 30m)]);

        portfolio.Lines.Select(line => line.TargetWeight).ShouldBe([70m, 30m]);
        portfolio.BaseCurrency.ShouldBe("NZD");
        portfolio.DomainEvents.OfType<PortfolioWeightsChanged>().ShouldHaveSingleItem();
    }

    [Test]
    public void ReplaceLines_DoesNotRaiseWhenTheWeightsAreUnchanged()
    {
        var portfolio = Portfolio.Create(1, "Balanced", Nzd(),
        [
            (10, 33.33m),
            (11, 33.33m),
            (12, 33.34m)
        ]);
        portfolio.ClearDomainEvents();

        portfolio.ReplaceLines(
        [
            (10, 33.33m),
            (11, 33.33m),
            (12, 33.33m)
        ]);

        portfolio.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Disable_IsIdempotentAndDoesNotRaiseAWeightChange()
    {
        var portfolio = Portfolio.Create(1, "Balanced", Nzd(), [(10, 100m)]);
        portfolio.ClearDomainEvents();

        portfolio.Disable();

        portfolio.IsActive.ShouldBeFalse();
        portfolio.Lines.Single().TargetWeight.ShouldBe(100m);
        portfolio.DomainEvents.ShouldBeEmpty();

        portfolio.Disable();

        portfolio.IsActive.ShouldBeFalse();
        portfolio.DomainEvents.ShouldBeEmpty();
    }

    private static Currency Nzd() => Currency.Create("NZD", "New Zealand Dollar", 2);
}
