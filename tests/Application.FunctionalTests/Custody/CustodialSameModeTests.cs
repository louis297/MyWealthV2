using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Transactions;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Custody modes R8, R9: both Accounts of a transfer, a conversion or a quote have the same custody mode.</summary>
public class CustodialSameModeTests : TestBase
{
    private const string TransferPath = "/transactions/transfer";
    private const string FxPath = "/transactions/fx";
    private const string QuotePath = "/transactions/fx/quote";
    private const string Message = "Both accounts must have the same custody mode.";
    private const decimal NzdUsd = 0.59120000m;

    [TestCase("Custodial", "External")]
    [TestCase("External", "Custodial")]
    public async Task Transfer_BetweenTwoCustodyModes_Returns400OnToAccountIdAndWritesNothing(string from, string to)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var fromId = await OpenAsync(tokens.AccessToken, customer.PublicId, from, "From", "NZD");
        var toId = await OpenAsync(tokens.AccessToken, customer.PublicId, to, "To", "NZD");
        (await FundAsync(tokens.AccessToken, fromId, 100m, external: from == "External")).StatusCode
            .ShouldBe(HttpStatusCode.Created);

        var response = await PostAsync(tokens.AccessToken, TransferBody(fromId, toId, 40m), TransferPath);

        await ShouldRefuseAsync(response);
        (await TransactionCountAsync(fromId)).ShouldBe(1);
        (await TransactionCountAsync(toId)).ShouldBe(0);
    }

    [Test]
    public async Task Transfer_BetweenTwoCustodialAccounts_Returns201_AndItsReverseReturns201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var fromId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "From", "NZD");
        var toId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "To", "NZD");
        (await FundAsync(tokens.AccessToken, fromId, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var transferId = await ReadIdAsync(
            await PostAsync(tokens.AccessToken, TransferBody(fromId, toId, 40m), TransferPath));
        (await CashBalanceAsync(tokens.AccessToken, toId)).ShouldBe(40m);

        (await ReverseAsync(tokens.AccessToken, transferId)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CashBalanceAsync(tokens.AccessToken, fromId)).ShouldBe(100m);
        (await CashBalanceAsync(tokens.AccessToken, toId)).ShouldBe(0m);
    }

    [TestCase("Custodial", "External")]
    [TestCase("External", "Custodial")]
    public async Task Conversion_BetweenTwoCustodyModes_Returns400OnToAccountIdAndWritesNothing(string from, string to)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var (fromId, toId) = await ConversionPairAsync(tokens.AccessToken, customer.PublicId, from, to);

        var response = await PostAsync(tokens.AccessToken, ConversionBody(fromId, toId, 100m), FxPath);

        await ShouldRefuseAsync(response);
        (await TransactionCountAsync(fromId)).ShouldBe(1);
        (await TransactionCountAsync(toId)).ShouldBe(0);
    }

    [Test]
    public async Task Conversion_BetweenTwoCustodialAccounts_Returns201_AndItsReverseReturns201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var (fromId, toId) = await ConversionPairAsync(tokens.AccessToken, customer.PublicId, "Custodial", "Custodial");

        var conversionId = await ReadIdAsync(
            await PostAsync(tokens.AccessToken, ConversionBody(fromId, toId, 100m), FxPath));
        (await CashBalanceAsync(tokens.AccessToken, toId)).ShouldBe(59.12m);

        (await ReverseAsync(tokens.AccessToken, conversionId)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CashBalanceAsync(tokens.AccessToken, fromId)).ShouldBe(1000m);
    }

    [TestCase("Custodial", "External")]
    [TestCase("External", "Custodial")]
    public async Task Quote_BetweenTwoCustodyModes_Returns400OnToAccountId(string from, string to)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var (fromId, toId) = await ConversionPairAsync(tokens.AccessToken, customer.PublicId, from, to);

        var response = await TransactionHttp.Send(
            HttpMethod.Post, QuotePath, tokens.AccessToken, QuoteBody(fromId, toId, 100m));

        await ShouldRefuseAsync(response);
    }

    [Test]
    public async Task Quote_BetweenTwoCustodialAccounts_Returns200()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var (fromId, toId) = await ConversionPairAsync(tokens.AccessToken, customer.PublicId, "Custodial", "Custodial");

        var response = await TransactionHttp.Send(
            HttpMethod.Post, QuotePath, tokens.AccessToken, QuoteBody(fromId, toId, 100m));

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("rate").GetDecimal().ShouldBe(NzdUsd);
    }

    [Test]
    public async Task Transfer_ToAnotherTenantsCustodialAccount_Returns404Not400()
    {
        var (_, _, customerA, tokensA) = await AccountHttp.SignInAdviser(
            "North Advisory", "north-advisory", "ann@north.example", customerEmail: "ned@north.example");
        var (_, _, customerB, tokensB) = await AccountHttp.SignInAdviser(
            "South Co", "south-co", "ann@south.example", customerEmail: "ned@south.example");
        var fromA = await OpenExternalAsync(tokensA.AccessToken, customerA.PublicId, currency: "NZD");
        var toB = await OpenCustodialAsync(tokensB.AccessToken, customerB.PublicId, currency: "NZD");
        (await FundAsync(tokensA.AccessToken, fromA, 100m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await PostAsync(
            tokensA.AccessToken, TransferBody(fromA, toB, 40m).WithBookedAt(), TransferPath);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await TransactionCountAsync(toB)).ShouldBe(0);
    }

    private static Task<Guid> OpenAsync(string accessToken, Guid customerId, string custodyMode, string name, string currency) =>
        custodyMode == "Custodial"
            ? OpenCustodialAsync(accessToken, customerId, name, currency)
            : OpenExternalAsync(accessToken, customerId, name, "Bank", currency);

    /// <summary>An NZD from Account funded with 1,000 and a USD to Account, with today's NZD → USD rate.</summary>
    private static async Task<(Guid From, Guid To)> ConversionPairAsync(
        string accessToken,
        Guid customerId,
        string from,
        string to)
    {
        await FxConversionTests.SetRateAsync("NZD", "USD", NzdUsd, DateOnly.FromDateTime(DateTime.UtcNow));
        var fromId = await OpenAsync(accessToken, customerId, from, "NZD side", "NZD");
        var toId = await OpenAsync(accessToken, customerId, to, "USD side", "USD");
        (await FundAsync(accessToken, fromId, 1000m, external: from == "External")).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        return (fromId, toId);
    }

    private static Dictionary<string, object?> TransferBody(Guid fromAccountId, Guid toAccountId, decimal amount) => new()
    {
        ["fromAccountId"] = fromAccountId,
        ["toAccountId"] = toAccountId,
        ["amount"] = amount
    };

    private static Dictionary<string, object?> ConversionBody(Guid fromAccountId, Guid toAccountId, decimal sellAmount) => new()
    {
        ["fromAccountId"] = fromAccountId,
        ["toAccountId"] = toAccountId,
        ["sellAmount"] = sellAmount,
        ["expectedRate"] = NzdUsd
    };

    private static Dictionary<string, object?> QuoteBody(Guid fromAccountId, Guid toAccountId, decimal sellAmount) => new()
    {
        ["fromAccountId"] = fromAccountId,
        ["toAccountId"] = toAccountId,
        ["sellAmount"] = sellAmount
    };

    private static async Task ShouldRefuseAsync(HttpResponseMessage response)
    {
        var errors = await ErrorsAsync(response);
        errors.ShouldContainKey("ToAccountId");
        errors["ToAccountId"].ShouldBe([Message]);
    }
}
