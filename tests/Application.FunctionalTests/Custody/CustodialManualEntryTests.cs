using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Custody modes R5 to R7: the manual entry matrix on <c>POST /transactions</c> and reverse.</summary>
[Parallelizable]
public class CustodialManualEntryTests : TestBase
{
    [Test]
    public async Task FundedByTheBankFeed_AWithdrawalTakesCashOut()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);

        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await WithdrawAsync(accountId, 250m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(750m);
    }

    [Test]
    public async Task AWithdrawalOfTheFullBalance_ThenTheAccountCloses()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        (await FundAsync(tokens.AccessToken, accountId, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await WithdrawAsync(accountId, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CloseAsync(tokens.AccessToken, accountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static IEnumerable<TestCaseData> RefusedTypes()
    {
        yield return new TestCaseData("Interest", "Interest cannot be entered by hand on a Custodial account.");
        yield return new TestCaseData("Opening", "Opening balances on a Custodial account come from a migration batch.");
        yield return new TestCaseData("OpeningHoldings", "Opening balances on a Custodial account come from a migration batch.");
        yield return new TestCaseData("Buy", "Trades on a Custodial account go through the broker. Place an order.");
        yield return new TestCaseData("Sell", "Trades on a Custodial account go through the broker. Place an order.");
        yield return new TestCaseData("StockIn", "Securities move in or out of a Custodial account by an in-kind transfer. Request a transfer.");
        yield return new TestCaseData("StockOut", "Securities move in or out of a Custodial account by an in-kind transfer. Request a transfer.");
        yield return new TestCaseData("Split", "Corporate actions cannot be entered by hand on a Custodial account.");
        yield return new TestCaseData("Bonus", "Corporate actions cannot be entered by hand on a Custodial account.");
        yield return new TestCaseData("Scrip", "Corporate actions cannot be entered by hand on a Custodial account.");
        // A Dividend is refused by the validator of POST /transactions (external-income R10); its Custodial message is
        // on POST /transactions/dividend (ExternalDividendTests).
    }

    [TestCaseSource(nameof(RefusedTypes))]
    public async Task RefusedType_Returns400OnTypeAndWritesNothing(string type, string message)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken);
        var key = Guid.NewGuid();

        var response = await PostAsync(tokens.AccessToken, RefusedBody(type, accountId, instrumentId), idempotencyKey: key);

        var errors = await ErrorsAsync(response);
        errors.ShouldContainKey("Type");
        errors["Type"].ShouldBe([message]);
        (await TransactionCountAsync(accountId)).ShouldBe(0);
        (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
    }

    [TestCase("Interest")]
    [TestCase("Opening")]
    [TestCase("OpeningHoldings")]
    [TestCase("StockIn")]
    public async Task ExternalAccount_StillAcceptsTheTypesACustodialAccountRefuses(string type)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenExternalAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken);

        var response = await PostAsync(tokens.AccessToken, RefusedBody(type, accountId, instrumentId).WithBookedAt());

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Reverse_OfAManualTransferInAndOfABuy_Returns201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, price: 25.00m);
        var fundingId = await InsertManualTransferInAsync(accountId, 1000m);
        var buyId = await InsertManualTradeAsync(accountId, "Buy", instrumentId, 4m);

        (await ReverseAsync(tokens.AccessToken, buyId)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReverseAsync(tokens.AccessToken, fundingId)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(0m);
    }

    [Test]
    public async Task AnotherTenantsCustodialAccount_Returns404Not400()
    {
        var (_, _, _, tokensA) = await AccountHttp.SignInAdviser(
            "North Advisory", "north-advisory", "ann@north.example", customerEmail: "ned@north.example");
        var (_, _, customerB, tokensB) = await AccountHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example", customerEmail: "ned@south.example");
        var accountB = await OpenCustodialAsync(tokensB.AccessToken, customerB.PublicId);

        var response = await PostAsync(tokensA.AccessToken, Cash(accountB, "Interest", 5m));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await TransactionCountAsync(accountB)).ShouldBe(0);
    }

    private static Dictionary<string, object?> RefusedBody(string type, Guid accountId, Guid instrumentId) => type switch
    {
        "Interest" or "Opening" => Cash(accountId, type, 100m),
        "OpeningHoldings" or "StockIn" => new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["type"] = type,
            ["security"] = new[] { new { instrumentId, quantity = 2m, cost = 50m } }
        },
        _ => Trade(accountId, type, instrumentId, 1m)
    };
}
