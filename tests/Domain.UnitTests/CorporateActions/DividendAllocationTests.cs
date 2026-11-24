using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.CorporateActions;

/// <summary>Dividends R28–R31: the shares of a reinvestment, their allocation, and when a dividend is paid.</summary>
public class DividendAllocationTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 25, 4, 10, 2, TimeSpan.Zero);

    [Test]
    public void Allocate_OneEntitlement_GetsEveryShare() =>
        DividendAllocation.Allocate(2.68m, [(7, 78.82352941m)]).ShouldBe([2.68m]);

    [Test]
    public void Allocate_ShareTheUnitsLeftOver_ByLargestRemainder_ThenByAccount()
    {
        var shares = DividendAllocation.Allocate(1.00000001m, [(9, 1m), (4, 1m), (6, 1m)]);

        // Each gets 0.33333333 with the same remainder; the two left-over units go to the two lowest account ids.
        shares.ShouldBe([0.33333333m, 0.33333334m, 0.33333334m]);
        shares.Sum().ShouldBe(1.00000001m);
    }

    [Test]
    public void Allocate_IsProportional_AndAddsUpExactly()
    {
        var shares = DividendAllocation.Allocate(2.94m, [(1, 78.82352941m), (2, 7.88235294m)]);

        shares.Sum().ShouldBe(2.94m);
        shares[0].ShouldBeGreaterThan(shares[1] * 9);
    }

    [Test]
    public void Allocate_WithNothingToShare_Throws() =>
        Should.Throw<DomainException>(() => DividendAllocation.Allocate(1m, []));

    private static CorporateAction Plan()
    {
        var action = CorporateAction.AnnounceDividend(1, 10, "simulator", "w-CA3", 1,
            DividendTerms.Create(1.00m, DividendTaxSource.Foreign, null, 15m, 25m), new DateOnly(2026, 11, 26), null, null);
        action.MakeEffective(At);
        return action;
    }

    [Test]
    public void TryPay_NeedsTheReceipt()
    {
        var action = Plan();
        action.SetElection(null, null);

        action.TryPay(At).ShouldBeFalse();
        action.TakeReceipt(5, 93.50m);
        action.TryPay(At).ShouldBeTrue();
        action.PaidAt.ShouldBe(At);
        action.TryPay(At).ShouldBeFalse();
    }

    [TestCase(ReinvestmentElectionStatus.Rejected, true)]
    [TestCase(ReinvestmentElectionStatus.Lapsed, true)]
    [TestCase(ReinvestmentElectionStatus.Pending, false)]
    [TestCase(ReinvestmentElectionStatus.Accepted, false)]
    public void TryPay_WithAReceipt_DependsOnTheElection(ReinvestmentElectionStatus status, bool paid)
    {
        var action = Plan();
        action.SetElection(78.82352941m, 3);
        switch (status)
        {
            case ReinvestmentElectionStatus.Rejected:
                action.RejectElection(ElectionRejectReason.TooLate);
                break;
            case ReinvestmentElectionStatus.Lapsed:
                action.LapseElection();
                break;
            case ReinvestmentElectionStatus.Accepted:
                action.AcceptElection();
                break;
        }

        action.TakeReceipt(5, 26.50m);

        action.TryPay(At).ShouldBe(paid);
    }

    [Test]
    public void TryPay_UnderAnAcceptedElection_NeedsTheShares_OrTheRelease()
    {
        var withShares = Plan();
        withShares.SetElection(78.82352941m, 3);
        withShares.AcceptElection();
        withShares.TakeReceipt(5, 26.50m);
        withShares.TakeShares(8, 2.68m);
        withShares.TryPay(At).ShouldBeTrue();

        var released = Plan();
        released.SetElection(78.82352941m, 3);
        released.AcceptElection();
        released.TakeReceipt(5, 26.50m);
        released.ReleaseElection(9, "The registry issued no shares", At);
        (released.ElectionStatus, released.ReleasedByUserId, released.ReleaseNote).ShouldBe(
            ((ReinvestmentElectionStatus?)ReinvestmentElectionStatus.Lapsed, (int?)9, "The registry issued no shares"));
        released.TryPay(At).ShouldBeTrue();
    }

    [Test]
    public void TakeShares_OnlyUnderAnAcceptedElection_AndOnce()
    {
        var action = Plan();
        action.SetElection(78.82352941m, 3);
        Should.Throw<DomainException>(() => action.TakeShares(8, 2.68m));
        action.AcceptElection();
        action.TakeShares(8, 2.68m);
        Should.Throw<DomainException>(() => action.TakeShares(9, 1m));
    }

    [Test]
    public void ReleaseElection_OnlyWhileItWaitsForShares()
    {
        var action = Plan();
        action.SetElection(78.82352941m, 3);
        action.AcceptElection();
        Should.Throw<DomainException>(() => action.ReleaseElection(9, "No shares", At));
        action.TakeReceipt(5, 26.50m);
        action.TakeShares(8, 2.68m);
        Should.Throw<DomainException>(() => action.ReleaseElection(9, "No shares", At));
    }

    [Test]
    public void SetReinvestedQuantity_OnlyOnAReinvestingEntitlement_AndOnce()
    {
        var reinvesting = CorporateActionEntitlement.CreateDividend(1, 5, 7, 100m, new DividendSourceAmounts(100m, 0m, 15m), true,
            78.82352941m, At);
        reinvesting.SetReinvestedQuantity(2.68m);
        reinvesting.ReinvestedQuantity.ShouldBe(2.68m);
        Should.Throw<DomainException>(() => reinvesting.SetReinvestedQuantity(1m));

        var cash = CorporateActionEntitlement.CreateDividend(1, 5, 8, 10m, new DividendSourceAmounts(10m, 0m, 1.5m), false, null, At);
        Should.Throw<DomainException>(() => cash.SetReinvestedQuantity(1m));
    }
}
