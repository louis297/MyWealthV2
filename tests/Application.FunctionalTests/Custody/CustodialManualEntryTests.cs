using System.Net;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Fees;
using MyWealthV2.Application.FunctionalTests.Transactions;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Custody modes R5 to R7: the manual entry matrix on <c>POST /transactions</c> and reverse.</summary>
[Parallelizable]
public class CustodialManualEntryTests : TestBase
{
    [Test]
    public async Task TransferInAndTransferOut_Return201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);

        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(tokens.AccessToken, Cash(accountId, "TransferOut", -250m))).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(750m);
    }

    [Test]
    public async Task BuyAndSell_Return201AtTheServerPrice()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, price: 25.00m);
        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await PostAsync(tokens.AccessToken, Trade(accountId, "Buy", instrumentId, 4m))).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(900m);

        (await PostAsync(tokens.AccessToken, Trade(accountId, "Sell", instrumentId, 1m))).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(925m);
    }

    [Test]
    public async Task Buy_WithATradeFeeSchedule_CarriesAFeeLeg()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, price: 25.00m);
        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await FeeHttp.AddScheduleAsync(tenant.Id, "USD", FeeHttp.ScheduleFrom);

        var buyId = await ReadIdAsync(await PostAsync(tokens.AccessToken, Trade(accountId, "Buy", instrumentId, 4m)));

        using var item = await GetTransactionAsync(tokens.AccessToken, buyId);
        TransactionJson.CashLegs(item.RootElement)
            .Select(leg => (leg.GetProperty("role").GetString(), leg.GetProperty("amount").GetDecimal()))
            .ShouldBe([("Principal", -100m), ("Fee", -10m)], ignoreOrder: true);
    }

    [Test]
    public async Task CloseOut_OfTheFullBalance_Returns201_ThenTheAccountCloses()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        (await FundAsync(tokens.AccessToken, accountId, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await PostAsync(tokens.AccessToken, Cash(accountId, "CloseOut", -100m))).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        (await CloseAsync(tokens.AccessToken, accountId)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static IEnumerable<TestCaseData> RefusedTypes()
    {
        yield return new TestCaseData("Interest", "Interest cannot be entered by hand on a Custodial account.");
        yield return new TestCaseData("Opening", "A Custodial account starts empty. Fund it with TransferIn.");
        yield return new TestCaseData("OpeningHoldings", "A Custodial account starts empty. Fund it with TransferIn.");
        yield return new TestCaseData("StockIn", "Securities cannot be moved in or out of a Custodial account by hand. Use Buy and Sell.");
        yield return new TestCaseData("StockOut", "Securities cannot be moved in or out of a Custodial account by hand. Use Buy and Sell.");
        yield return new TestCaseData("Split", "Corporate actions cannot be entered by hand on a Custodial account.");
        yield return new TestCaseData("Bonus", "Corporate actions cannot be entered by hand on a Custodial account.");
        yield return new TestCaseData("Scrip", "Corporate actions cannot be entered by hand on a Custodial account.");
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
    public async Task Reverse_OfATransferInAndOfABuy_Returns201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, price: 25.00m);
        var fundingId = await ReadIdAsync(await FundAsync(tokens.AccessToken, accountId, 1000m));
        var buyId = await ReadIdAsync(await PostAsync(tokens.AccessToken, Trade(accountId, "Buy", instrumentId, 4m)));

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
