using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using static MyWealthV2.Application.FunctionalTests.Custody.CustodialHttp;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>Adjustments §8.2: every refusal of an adjustment, on <c>POST /adjustment-requests</c> (adjustment-review R4),
/// idempotency and isolation.</summary>
[Parallelizable]
public class AdjustmentRulesTests : TestBase
{
    private const string BookedAtMessage = "bookedAt cannot be set on a Custodial account. The server sets it.";
    private const string ExternalMessage = "An adjustment can be entered only on a Custodial account.";
    private const string HoldingMessage =
        "The holding cannot have a negative quantity or cost, or a cost without a quantity.";

    private static IEnumerable<TestCaseData> RefusedFields()
    {
        yield return new TestCaseData("tenantId", (object)Guid.NewGuid(), "TenantId").SetName("Refuses_tenantId");
        yield return new TestCaseData("bookedAt", (object)"2026-10-11T10:00:00Z", "BookedAt").SetName("Refuses_bookedAt");
        yield return new TestCaseData("type", (object)"Adjustment", "Type").SetName("Refuses_type");
        yield return new TestCaseData("currency", (object)"USD", "Currency").SetName("Refuses_currency");
    }

    [TestCaseSource(nameof(RefusedFields))]
    public async Task FieldThatIsNotPartOfTheBody_Returns400AtThatField(string field, object value, string key)
    {
        var world = await AdjustmentWorld.CreateAsync();
        var body = AdjustmentWorld.Body(world.AccountId, amount: 5m);
        body[field] = value;

        var errors = await ErrorsAsync(await world.AdjustAsync(body));

        errors.ShouldContainKey(key);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task BookedAt_Returns400WithTheCustodyModesMessage()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var body = AdjustmentWorld.Body(world.AccountId, amount: 5m);
        body["bookedAt"] = "2026-10-11T10:00:00Z";

        var errors = await ErrorsAsync(await world.AdjustAsync(body));

        errors["BookedAt"].ShouldBe([BookedAtMessage]);
    }

    [Test]
    public async Task CurrencyOnTheHoldingPart_Returns400()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var body = AdjustmentWorld.Body(
            world.AccountId,
            security: new { instrumentId = world.InstrumentId, quantityChange = 1m, costChange = 25m, currency = "USD" });

        var errors = await ErrorsAsync(await world.AdjustAsync(body));

        errors.Keys.ShouldContain(key => key.StartsWith("Security", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task NeitherAmountNorSecurity_Returns400()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var errors = await ErrorsAsync(await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId)));

        errors.Values.SelectMany(messages => messages).ShouldContain("At least one of amount and security is required.");
    }

    [Test]
    public async Task ZeroAmount_Returns400AtAmount()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var errors = await ErrorsAsync(await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 0m)));

        errors.ShouldContainKey("Amount");
    }

    [Test]
    public async Task BothChangesZero_Returns400AtSecurity()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var errors = await ErrorsAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, 0m, 0m))));

        errors.ShouldContainKey("Security");
    }

    [Test]
    public async Task AmountWithMorePlacesThanTheCurrency_Returns400AtAmount()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var errors = await ErrorsAsync(await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 1.234m)));

        errors.ShouldContainKey("Amount");
    }

    [TestCase(0.123456789, 0, "Security.QuantityChange")]
    [TestCase(1, 0.12345, "Security.CostChange")]
    public async Task ChangeWithTooManyDecimalPlaces_Returns400AtThatChange(
        decimal quantityChange,
        decimal costChange,
        string key)
    {
        var world = await AdjustmentWorld.CreateAsync();

        var errors = await ErrorsAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, quantityChange, costChange))));

        errors.ShouldContainKey(key);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("entryError")]
    [TestCase("Rounding")]
    [TestCase("0")]
    public async Task MissingOrUnknownReason_Returns400AtReason(string? reason)
    {
        var world = await AdjustmentWorld.CreateAsync();
        var body = AdjustmentWorld.Body(world.AccountId, amount: 5m);
        body["reason"] = reason;

        var errors = await ErrorsAsync(await world.AdjustAsync(body));

        errors.ShouldContainKey("Reason");
        (await TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task MissingOrBlankMemo_Returns400AtMemo(string? memo)
    {
        var world = await AdjustmentWorld.CreateAsync();
        var body = AdjustmentWorld.Body(world.AccountId, amount: 5m);
        body["memo"] = memo;

        var errors = await ErrorsAsync(await world.AdjustAsync(body));

        errors.ShouldContainKey("Memo");
    }

    [Test]
    public async Task MemoOverTwoHundredCharacters_Returns400AtMemo()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var errors = await ErrorsAsync(await world.AdjustAsync(
            AdjustmentWorld.Body(world.AccountId, amount: 5m, memo: new string('m', 201))));

        errors.ShouldContainKey("Memo");
    }

    [Test]
    public async Task ExternalAccount_Returns400AtAccountId_AndWritesNothing()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var key = Guid.NewGuid();

        var response = await world.AdjustAsync(AdjustmentWorld.Body(world.ExternalAccountId, amount: 5m), key);

        (await ErrorsAsync(response))["AccountId"].ShouldBe([ExternalMessage]);
        (await TransactionCountAsync(world.ExternalAccountId)).ShouldBe(0);
        (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
    }

    [Test]
    public async Task CashBelowZero_Returns400AndWritesNothing()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await world.FundAsync(10m);
        var key = Guid.NewGuid();

        var response = await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: -10.01m), key);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await world.CashAsync()).ShouldBe(10m);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(1);
        (await IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
    }

    [TestCase(-11, 0)]
    [TestCase(0, -251)]
    [TestCase(-10, 0)]
    public async Task HoldingResultRefused_Returns400AtSecurity(decimal quantityChange, decimal costChange)
    {
        var world = await AdjustmentWorld.CreateAsync();
        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, 10m, 250m))));

        var errors = await ErrorsAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, quantityChange, costChange))));

        errors["Security"].ShouldBe([HoldingMessage]);
        (await world.HoldingAsync()).ShouldBe((10m, 250m));
    }

    [Test]
    public async Task CostOnlyChangeWithoutAHolding_Returns400AtSecurity()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var errors = await ErrorsAsync(await world.AdjustAsync(AdjustmentWorld.Body(
            world.AccountId, security: AdjustmentWorld.Holding(world.InstrumentId, 0m, 5m))));

        errors["Security"].ShouldBe([HoldingMessage]);
        (await world.HoldingAsync()).ShouldBeNull();
    }

    [Test]
    public async Task ClosedAccount_Returns400()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await LegAccountsFixture.CloseAccountDirectlyAsync(world.AccountId);

        var response = await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 5m));

        (await ErrorsAsync(response)).Values.SelectMany(messages => messages)
            .ShouldContain("Cannot post a transaction while the account is closed. Reopen the account first.");
        (await TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task DisabledCustomer_Returns400TargetUser()
    {
        var world = await AdjustmentWorld.CreateAsync();
        await LegAccountsFixture.DisableCustomerAsync(world.Customer.Id);

        var response = await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 5m));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        (await TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task UnknownAccountOrInstrument_Returns404()
    {
        var world = await AdjustmentWorld.CreateAsync();

        (await world.AdjustAsync(AdjustmentWorld.Body(Guid.NewGuid(), amount: 5m)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await world.AdjustAsync(AdjustmentWorld.Body(
                world.AccountId, security: AdjustmentWorld.Holding(Guid.NewGuid(), 1m, 25m))))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task MissingIdempotencyKey_Returns400()
    {
        var world = await AdjustmentWorld.CreateAsync();

        var response = await TransactionHttp.Send(
            HttpMethod.Post, AdjustmentWorld.Path, world.Admin, AdjustmentWorld.Body(world.AccountId, amount: 5m));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task SameKeyDifferentBody_Returns409()
    {
        var world = await AdjustmentWorld.CreateAsync();
        var key = Guid.NewGuid();
        await ReadIdAsync(await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 5m), key));

        var response = await world.AdjustAsync(AdjustmentWorld.Body(world.AccountId, amount: 6m), key);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await TransactionCountAsync(world.AccountId)).ShouldBe(1);
    }

    [Test]
    public async Task OtherTenantsAccountOrInstrument_Returns404AndWritesNothing()
    {
        var north = await AdjustmentWorld.CreateAsync("adjust-north");
        var south = await AdjustmentWorld.CreateAsync("adjust-south");

        (await north.AdjustAsync(AdjustmentWorld.Body(south.AccountId, amount: 5m)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await north.AdjustAsync(AdjustmentWorld.Body(
                north.AccountId, security: AdjustmentWorld.Holding(south.InstrumentId, 1m, 25m))))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await TransactionCountAsync(north.AccountId)).ShouldBe(0);
        (await TransactionCountAsync(south.AccountId)).ShouldBe(0);
    }
}
