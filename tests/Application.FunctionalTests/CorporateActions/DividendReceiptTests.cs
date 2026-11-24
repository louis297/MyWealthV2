using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.StatementLines;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>Dividends R24, R25, R49, R53: the custodian's payment of a dividend on the client money account.</summary>
[Parallelizable]
public class DividendReceiptTests : TestBase
{
    // Ned 100 and Olga 10 shares at 1.00 less 15 % foreign tax: 85.00 + 8.50.
    private const decimal Expected = 93.50m;

    [Test]
    public async Task AReceiptNamingTheDividend_WithTheExpectedAmount_IsMatchedToIt_WithNoAccountAndNoTransaction()
    {
        var dividend = await DividendWorld.CreateAsync();

        var line = await dividend.ReceiveAsync(Expected);

        (line.Status, line.MatchMethod, line.MatchedAccountId, line.MatchedTransactionId).ShouldBe((
            BankStatementLineStatus.Matched, (StatementLineMatchMethod?)StatementLineMatchMethod.Dividend, (int?)null, (int?)null));
        var action = await dividend.DividendAsync();
        line.CorporateActionId.ShouldBe(action.Id);
        (action.ReceiptLineId, action.ReceivedAmount).ShouldBe(((int?)line.Id, (decimal?)Expected));
        (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).ShouldBeEmpty();

        var payment = (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("payment");
        payment.GetProperty("expectedAmount").GetDecimal().ShouldBe(Expected);
        payment.GetProperty("receivedAmount").GetDecimal().ShouldBe(Expected);
        payment.GetProperty("difference").GetDecimal().ShouldBe(0m);
        payment.GetProperty("statementLineId").GetGuid().ShouldBe(line.PublicId);
    }

    [Test]
    public async Task TheReference_IsComparedTrimmedAndWithoutRegardToCase()
    {
        var dividend = await DividendWorld.CreateAsync();

        var line = await dividend.ReceiveAsync(Expected, "  W-ca3 ");

        line.MatchMethod.ShouldBe(StatementLineMatchMethod.Dividend);
    }

    [Test]
    public async Task AReceiptWithinTheRoundingAllowance_IsMatched_AndTheDifferenceStaysVisible()
    {
        var dividend = await DividendWorld.CreateAsync();

        // Two entitlements: an allowance of 4 minor units.
        var line = await dividend.ReceiveAsync(Expected + 0.04m);

        line.MatchMethod.ShouldBe(StatementLineMatchMethod.Dividend);
        (await ItemAsync(dividend.TenantAdmin, dividend.DividendId)).GetProperty("payment").GetProperty("difference")
            .GetDecimal().ShouldBe(0.04m);
    }

    [TestCase(0.05)]
    [TestCase(-0.05)]
    public async Task AReceiptOutsideTheAllowance_IsADividendMismatch_AndTheDividendDoesNotChange(decimal off)
    {
        var dividend = await DividendWorld.CreateAsync();

        var line = await dividend.ReceiveAsync(Expected + off);

        (line.Status, line.UnmatchedReason, line.CorporateActionId).ShouldBe(
            (BankStatementLineStatus.Unmatched, (UnmatchedReason?)UnmatchedReason.DividendMismatch, (int?)null));
        (await dividend.DividendAsync()).ReceiptLineId.ShouldBeNull();
    }

    [Test]
    public async Task AReceiptForADividendThatHasNotGoneEx_IsADividendMismatch()
    {
        var announced = await DividendWorld.CreateAsync(goEx: false);
        (await announced.ReceiveAsync(Expected)).UnmatchedReason.ShouldBe(UnmatchedReason.DividendMismatch);
        (await announced.DividendAsync()).ReceiptLineId.ShouldBeNull();
    }

    [Test]
    public async Task ASecondReceipt_IsADividendMismatch()
    {
        var dividend = await DividendWorld.CreateAsync();
        var first = await dividend.ReceiveAsync(Expected);

        var second = await dividend.ReceiveAsync(Expected);

        second.UnmatchedReason.ShouldBe(UnmatchedReason.DividendMismatch);
        (await dividend.DividendAsync()).ReceiptLineId.ShouldBe(first.Id);
    }

    [Test]
    public async Task AReceiptInAnotherCurrency_IsNotADividendReceipt()
    {
        var dividend = await DividendWorld.CreateAsync();

        var line = await dividend.ReceiveAsync(Expected, currency: "NZD");

        line.MatchMethod.ShouldBeNull();
        line.UnmatchedReason.ShouldNotBe(UnmatchedReason.DividendMismatch);
        (await dividend.DividendAsync()).ReceiptLineId.ShouldBeNull();
    }

    [Test]
    public async Task TheStatementLineItem_NamesTheDividend_AndAMismatchShowsItsReason()
    {
        var dividend = await DividendWorld.CreateAsync();
        var matched = await dividend.ReceiveAsync(Expected);
        var mismatch = await dividend.ReceiveAsync(1m);

        var item = await StatementLineHttp.GetAsync(dividend.TenantAdmin, matched.PublicId);
        item.GetProperty("corporateActionId").GetGuid().ShouldBe(dividend.DividendId);
        item.GetProperty("match").GetProperty("method").GetString().ShouldBe("Dividend");
        var other = await StatementLineHttp.GetAsync(dividend.TenantAdmin, mismatch.PublicId);
        other.GetProperty("corporateActionId").ValueKind.ShouldBe(JsonValueKind.Null);
        other.GetProperty("unmatchedReason").GetString().ShouldBe("DividendMismatch");
    }

    [Test]
    public async Task AnAdviser_SeesThePaymentWithoutTheStatementLine()
    {
        var dividend = await DividendWorld.CreateAsync();
        await dividend.ReceiveAsync(Expected);

        var payment = (await ItemAsync(dividend.World.AdviserToken.AccessToken, dividend.DividendId)).GetProperty("payment");

        payment.GetProperty("receivedAmount").GetDecimal().ShouldBe(Expected);
        payment.GetProperty("statementLineId").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
