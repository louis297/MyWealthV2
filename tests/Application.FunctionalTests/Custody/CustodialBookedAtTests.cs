using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Transactions;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Custody modes R10–R12: the server sets bookedAt on a Custodial account; External keeps it required.</summary>
public class CustodialBookedAtTests : TestBase
{
    private const string TransferPath = "/transactions/transfer";
    private const string FxPath = "/transactions/fx";
    private const string QuotePath = "/transactions/fx/quote";
    private const string CustodialMessage = "bookedAt cannot be set on a Custodial account. The server sets it.";
    private const decimal NzdUsd = 0.59120000m;

    [Test]
    public async Task Custodial_TransferInWithoutBookedAt_Returns201_BookedAtTheServerClock()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);

        var before = DateTimeOffset.UtcNow;
        var id = await ReadIdAsync(await FundAsync(tokens.AccessToken, accountId, 100m));
        var after = DateTimeOffset.UtcNow;

        using var item = await GetTransactionAsync(tokens.AccessToken, id);
        var bookedAt = item.RootElement.GetProperty("bookedAt").GetDateTimeOffset();
        bookedAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        bookedAt.ShouldBeLessThanOrEqualTo(after.AddSeconds(1));
        item.RootElement.GetProperty("bookedAt").GetString()!.ShouldEndWith("Z");
    }

    private static IEnumerable<TestCaseData> SentInstants()
    {
        yield return new TestCaseData(0, CustodialMessage).SetName("Custodial_BookedAtNow_Returns400AndWritesNothing");
        yield return new TestCaseData(-86400, CustodialMessage).SetName("Custodial_BookedAtInThePast_Returns400AndWritesNothing");
        yield return new TestCaseData(3600, null).SetName("Custodial_BookedAtInTheFuture_Returns400AndWritesNothing");
    }

    [TestCaseSource(nameof(SentInstants))]
    public async Task Custodial_TransferInWithBookedAt_Returns400OnBookedAtAndWritesNothing(int offsetSeconds, string? message)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var key = Guid.NewGuid();

        var response = await PostAsync(
            tokens.AccessToken,
            Cash(accountId, "TransferIn", 100m).WithBookedAt(Instant(DateTimeOffset.UtcNow.AddSeconds(offsetSeconds))),
            idempotencyKey: key);

        await ShouldRefuseBookedAtAsync(response, message);
        (await TransactionCountAsync(accountId)).ShouldBe(0);
        (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
    }

    [Test]
    public async Task Custodial_BookedAtNull_IsTheSameAsLeavingItOut()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var body = Cash(accountId, "TransferIn", 100m);
        body["bookedAt"] = null;

        (await PostAsync(tokens.AccessToken, body)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Custodial_Buy_IsBookedAndPricedAtTheServerClock()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var instrumentId = await CreateInstrumentAsync(tokens.AccessToken, price: 25.00m);
        (await FundAsync(tokens.AccessToken, accountId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var before = DateTimeOffset.UtcNow;
        var buyId = await ReadIdAsync(await PostAsync(tokens.AccessToken, Trade(accountId, "Buy", instrumentId, 2m)));

        using var item = await GetTransactionAsync(tokens.AccessToken, buyId);
        item.RootElement.GetProperty("bookedAt").GetDateTimeOffset().ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        (await CashBalanceAsync(tokens.AccessToken, accountId)).ShouldBe(950m);
    }

    [Test]
    public async Task Custodial_TransferWithBookedAt_Returns400AndWritesNothing()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var fromId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "From", "NZD");
        var toId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId, "To", "NZD");
        (await FundAsync(tokens.AccessToken, fromId, 100m)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await PostAsync(
            tokens.AccessToken, TransferBody(fromId, toId).WithBookedAt(Instant(DateTimeOffset.UtcNow)), TransferPath);

        await ShouldRefuseBookedAtAsync(response, CustodialMessage);
        (await TransactionCountAsync(toId)).ShouldBe(0);
    }

    [Test]
    public async Task Custodial_ConversionWithBookedAt_Returns400AndWritesNothing()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var (fromId, toId) = await CustodialConversionPairAsync(tokens.AccessToken, customer.PublicId);

        var response = await PostAsync(
            tokens.AccessToken, ConversionBody(fromId, toId).WithBookedAt(Instant(DateTimeOffset.UtcNow)), FxPath);

        await ShouldRefuseBookedAtAsync(response, CustodialMessage);
        (await TransactionCountAsync(toId)).ShouldBe(0);
    }

    [Test]
    public async Task Custodial_ConversionWithoutBookedAt_Returns201()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var (fromId, toId) = await CustodialConversionPairAsync(tokens.AccessToken, customer.PublicId);

        (await PostAsync(tokens.AccessToken, ConversionBody(fromId, toId), FxPath)).StatusCode
            .ShouldBe(HttpStatusCode.Created);
        (await CashBalanceAsync(tokens.AccessToken, toId)).ShouldBe(59.12m);
    }

    [Test]
    public async Task Custodial_QuoteWithBookedAt_Returns400_WithoutReturns200()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var (fromId, toId) = await CustodialConversionPairAsync(tokens.AccessToken, customer.PublicId);
        var quote = new Dictionary<string, object?>
        {
            ["fromAccountId"] = fromId,
            ["toAccountId"] = toId,
            ["sellAmount"] = 100m
        };

        var withoutBookedAt = await TransactionHttp.Send(HttpMethod.Post, QuotePath, tokens.AccessToken, quote);
        withoutBookedAt.StatusCode.ShouldBe(HttpStatusCode.OK, await withoutBookedAt.Content.ReadAsStringAsync());

        quote["bookedAt"] = Instant(DateTimeOffset.UtcNow.AddDays(-1));
        await ShouldRefuseBookedAtAsync(
            await TransactionHttp.Send(HttpMethod.Post, QuotePath, tokens.AccessToken, quote), CustodialMessage);
    }

    [Test]
    public async Task Custodial_ReplayWithTheSameIdempotencyKey_ReturnsTheSameIdAndWritesOneTransaction()
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var accountId = await OpenCustodialAsync(tokens.AccessToken, customer.PublicId);
        var key = Guid.NewGuid();

        var first = await ReadIdAsync(await PostAsync(tokens.AccessToken, Cash(accountId, "TransferIn", 100m), idempotencyKey: key));
        var second = await ReadIdAsync(await PostAsync(tokens.AccessToken, Cash(accountId, "TransferIn", 100m), idempotencyKey: key));

        second.ShouldBe(first);
        (await TransactionCountAsync(accountId)).ShouldBe(1);
    }

    [TestCase("/transactions")]
    [TestCase(TransferPath)]
    [TestCase(FxPath)]
    public async Task External_CreateWithoutBookedAt_Returns400OnBookedAtAndWritesNothing(string route)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var nzd = await OpenExternalAsync(tokens.AccessToken, customer.PublicId, "NZD bank", "Bank", "NZD");
        var savings = await OpenExternalAsync(tokens.AccessToken, customer.PublicId, "NZD savings", "Bank", "NZD");
        var usd = await OpenExternalAsync(tokens.AccessToken, customer.PublicId, "USD bank", "Bank", "USD");
        await FxConversionTests.SetRateAsync("NZD", "USD", NzdUsd, DateOnly.FromDateTime(DateTime.UtcNow));
        (await FundAsync(tokens.AccessToken, nzd, 1000m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = route switch
        {
            "/transactions" => Cash(nzd, "TransferIn", 10m),
            TransferPath => TransferBody(nzd, savings),
            _ => ConversionBody(nzd, usd)
        };

        var response = await PostAsync(tokens.AccessToken, body, route);

        var errors = await ErrorsAsync(response);
        errors.ShouldContainKey("BookedAt");
        (await TransactionCountAsync(nzd)).ShouldBe(1);
    }

    [TestCase("/transactions")]
    [TestCase(TransferPath)]
    public async Task External_CreateWithAPastBookedAt_Returns201(string route)
    {
        var (_, _, customer, tokens) = await AccountHttp.SignInAdviser();
        var nzd = await OpenExternalAsync(tokens.AccessToken, customer.PublicId, "NZD bank", "Bank", "NZD");
        var savings = await OpenExternalAsync(tokens.AccessToken, customer.PublicId, "NZD savings", "Bank", "NZD");
        (await FundAsync(tokens.AccessToken, nzd, 1000m, external: true)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = route == TransferPath ? TransferBody(nzd, savings) : Cash(nzd, "TransferIn", 10m);

        var id = await ReadIdAsync(await PostAsync(tokens.AccessToken, body.WithBookedAt("2026-10-12T10:00:00Z"), route));

        using var item = await GetTransactionAsync(tokens.AccessToken, id);
        item.RootElement.GetProperty("bookedAt").GetString().ShouldBe("2026-10-12T10:00:00Z");
    }

    private static async Task<(Guid From, Guid To)> CustodialConversionPairAsync(string accessToken, Guid customerId)
    {
        await FxConversionTests.SetRateAsync("NZD", "USD", NzdUsd, DateOnly.FromDateTime(DateTime.UtcNow));
        var fromId = await OpenCustodialAsync(accessToken, customerId, "NZD side", "NZD");
        var toId = await OpenCustodialAsync(accessToken, customerId, "USD side", "USD");
        (await FundAsync(accessToken, fromId, 1000m)).StatusCode.ShouldBe(HttpStatusCode.Created);
        return (fromId, toId);
    }

    private static Dictionary<string, object?> TransferBody(Guid fromAccountId, Guid toAccountId) => new()
    {
        ["fromAccountId"] = fromAccountId,
        ["toAccountId"] = toAccountId,
        ["amount"] = 40m
    };

    private static Dictionary<string, object?> ConversionBody(Guid fromAccountId, Guid toAccountId) => new()
    {
        ["fromAccountId"] = fromAccountId,
        ["toAccountId"] = toAccountId,
        ["sellAmount"] = 100m,
        ["expectedRate"] = NzdUsd
    };

    private static string Instant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    /// <summary>400 on BookedAt; with <paramref name="message"/> null any BookedAt message (the future rule may answer first).</summary>
    private static async Task ShouldRefuseBookedAtAsync(HttpResponseMessage response, string? message)
    {
        var errors = await ErrorsAsync(response);
        errors.ShouldContainKey("BookedAt");
        if (message is not null)
        {
            errors["BookedAt"].ShouldBe([message]);
        }
    }
}
