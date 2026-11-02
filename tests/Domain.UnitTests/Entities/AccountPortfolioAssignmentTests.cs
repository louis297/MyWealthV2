using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class AccountPortfolioAssignmentTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void Assign_CreatesOneActiveAssignment()
    {
        var assignment = AccountPortfolioAssignment.Assign(null, 4, 8, 15, "adviser", At);

        assignment.TenantId.ShouldBe(4);
        assignment.AccountId.ShouldBe(8);
        assignment.PortfolioId.ShouldBe(15);
        assignment.Status.ShouldBe(AssignmentStatus.Active);
        assignment.AssignedBy.ShouldBe("adviser");
        assignment.AssignedAt.ShouldBe(At);
        assignment.DomainEvents.OfType<PortfolioAssigned>().ShouldHaveSingleItem();
    }

    [Test]
    public void Assign_RemovesThePreviousActivePortfolio()
    {
        var current = AccountPortfolioAssignment.Assign(null, 4, 8, 15, "adviser", At);
        current.ClearDomainEvents();

        var next = AccountPortfolioAssignment.Assign(current, 4, 8, 16, "adviser", At.AddMinutes(1));

        current.Status.ShouldBe(AssignmentStatus.Removed);
        current.DomainEvents.OfType<PortfolioUnassigned>().ShouldHaveSingleItem();
        next.Status.ShouldBe(AssignmentStatus.Active);
        next.PortfolioId.ShouldBe(16);
        next.DomainEvents.OfType<PortfolioAssigned>().ShouldHaveSingleItem();
    }

    [Test]
    public void Assign_RejectsTheSameActivePortfolio()
    {
        var current = AccountPortfolioAssignment.Assign(null, 4, 8, 15, "adviser", At);
        current.ClearDomainEvents();

        Should.Throw<DomainException>(() =>
            AccountPortfolioAssignment.Assign(current, 4, 8, 15, "adviser", At.AddMinutes(1)));

        current.Status.ShouldBe(AssignmentStatus.Active);
        current.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void Remove_MarksRemovedAndIsIdempotent()
    {
        var current = AccountPortfolioAssignment.Assign(null, 4, 8, 15, "adviser", At);
        current.ClearDomainEvents();

        current.Remove();

        current.Status.ShouldBe(AssignmentStatus.Removed);
        current.DomainEvents.OfType<PortfolioUnassigned>().ShouldHaveSingleItem();

        current.ClearDomainEvents();
        current.Remove();

        current.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public void RecordRebalance_StoresTheOutcome()
    {
        var current = AccountPortfolioAssignment.Assign(null, 4, 8, 15, "adviser", At);

        current.RecordRebalance(RebalanceStatus.Failed, "missing price VTI", At.AddHours(1));

        current.LastRebalanceStatus.ShouldBe(RebalanceStatus.Failed);
        current.LastRebalanceMessage.ShouldBe("missing price VTI");
        current.LastRebalanceAt.ShouldBe(At.AddHours(1));
        current.Status.ShouldBe(AssignmentStatus.Active);
    }
}
