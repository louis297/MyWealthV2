using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Reports;
using MyWealthV2.Application.FunctionalTests.Tenants;
using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using UglyToad.PdfPig;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>
/// External income R1–R21, R26, R27: <c>POST /transactions/dividend</c> records a dividend an outside party paid into
/// an External account, with the caller's gross, taxes, imputation credit and FX rate (§1 examples A–D).
/// </summary>
[Parallelizable]
public class ExternalDividendTests : TestBase
{
    internal const string DividendPath = "/transactions/dividend";
    private const string BookedAt = "2026-11-20T00:00:00Z";
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task NoBearer_Returns401Not302()
    {
        var world = await WorldAsync("NZD", "NZD");

        var response = await TransactionHttp.Send(HttpMethod.Post, DividendPath, null, ExampleA(world), Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var world = await WorldAsync("NZD", "NZD");
        var customer = await TestApp.IssueAccessTokenAsync(world.Customer);

        (await PostAsync(customer.AccessToken, ExampleA(world))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await AssertNothingWrittenAsync(world);
    }

    [Test]
    public async Task ExampleA_ANewZealandCompany_BooksTheGrossAndTheRwt_WithTheCreditInTheDetail()
    {
        var world = await WorldAsync("NZD", "NZD");

        var response = await PostAsync(world.Token, ExampleA(world));

        var id = await CustodialHttp.ReadIdAsync(response);
        response.Headers.Location!.ToString().ShouldBe($"/transactions/{id}");
        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        var root = item.RootElement;
        root.GetProperty("type").GetString().ShouldBe("Dividend");
        root.GetProperty("bookedAt").GetDateTimeOffset().ShouldBe(DateTimeOffset.Parse(BookedAt));
        root.GetProperty("source").GetProperty("kind").GetString().ShouldBe("Manual");
        root.GetProperty("source").GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("fx").ValueKind.ShouldBe(JsonValueKind.Null);
        TransactionJson.SecurityLegs(root).ShouldBeEmpty();
        Legs(root).ShouldBe([("Principal", 2000.00m, null), ("Tax", -526.00m, "Rwt")], ignoreOrder: true);
        Tax(root, "Rwt").ShouldBe((33m, 2200.00m, 526.00m, "NZD"));
        var dividend = root.GetProperty("dividend");
        dividend.GetProperty("corporateActionId").ValueKind.ShouldBe(JsonValueKind.Null);
        dividend.GetProperty("instrument").GetProperty("id").GetGuid().ShouldBe(world.InstrumentId);
        dividend.GetProperty("instrument").GetProperty("symbol").GetString().ShouldBe("AIA");
        dividend.GetProperty("quantity").GetDecimal().ShouldBe(1000m);
        dividend.GetProperty("amountPerShare").ValueKind.ShouldBe(JsonValueKind.Null);
        dividend.GetProperty("currency").GetString().ShouldBe("NZD");
        dividend.GetProperty("taxSource").GetString().ShouldBe("NewZealand");
        dividend.GetProperty("imputationCredit").GetDecimal().ShouldBe(200.00m);
        dividend.GetProperty("reinvestedQuantity").ValueKind.ShouldBe(JsonValueKind.Null);
        dividend.GetProperty("reinvestmentPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        dividend.GetProperty("exDate").ValueKind.ShouldBe(JsonValueKind.Null);
        dividend.GetProperty("recordDate").ValueKind.ShouldBe(JsonValueKind.Null);
        dividend.GetProperty("paymentDate").ValueKind.ShouldBe(JsonValueKind.Null);
        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(1474.00m);
    }

    [Test]
    public async Task ExampleB_AForeignCompany_BooksOneTaxLegPerTax()
    {
        var world = await WorldAsync("USD", "USD");
        var body = Body(world, 100m, 100.00m, "Foreign",
            Taxes(("ForeignWithholding", 15m, 15.00m), ("Rwt", 33m, 18.00m)));

        var id = await CustodialHttp.ReadIdAsync(await PostAsync(world.Token, body));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        Legs(item.RootElement).ShouldBe(
            [("Principal", 100.00m, null), ("Tax", -15.00m, "ForeignWithholding"), ("Tax", -18.00m, "Rwt")],
            ignoreOrder: true);
        Tax(item.RootElement, "ForeignWithholding").ShouldBe((15m, 100.00m, 15.00m, "USD"));
        Tax(item.RootElement, "Rwt").ShouldBe((33m, 100.00m, 18.00m, "USD"));
        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(67.00m);
    }

    [Test]
    public async Task ExampleC_AnotherCurrency_IsConvertedAtTheCallersRate_WithAnFxDetail_AndEveryOptionalField()
    {
        var world = await WorldAsync("NZD", "USD", symbol: "AAPL");
        var body = ExampleC(world);

        var id = await CustodialHttp.ReadIdAsync(await PostAsync(world.Token, body));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        var root = item.RootElement;
        Legs(root).ShouldBe([("Principal", 4.40m, null), ("Tax", -0.66m, "ForeignWithholding")], ignoreOrder: true);
        Tax(root, "ForeignWithholding").ShouldBe((15m, 4.40m, 0.66m, "NZD"));
        var fx = root.GetProperty("fx");
        (fx.GetProperty("baseCurrency").GetString(), fx.GetProperty("quoteCurrency").GetString(),
                fx.GetProperty("rate").GetDecimal(), fx.GetProperty("rateAsOfDate").GetString())
            .ShouldBe(("USD", "NZD", 1.69147m, "2026-11-20"));
        (await FxConversionTests.FxDetailAsync(id)).ShouldBe(("USD", "NZD", 1.69147m, new DateOnly(2026, 11, 20), "Caller"));
        (root.GetProperty("memo").GetString(), root.GetProperty("reference").GetString())
            .ShouldBe(("Quarterly dividend", "HATCH-2026-11"));
        var dividend = root.GetProperty("dividend");
        dividend.GetProperty("amountPerShare").GetDecimal().ShouldBe(0.26m);
        dividend.GetProperty("currency").GetString().ShouldBe("USD");
        dividend.GetProperty("taxSource").GetString().ShouldBe("Foreign");
        dividend.GetProperty("imputationCredit").GetDecimal().ShouldBe(0m);
        (dividend.GetProperty("exDate").GetString(), dividend.GetProperty("recordDate").GetString(),
                dividend.GetProperty("paymentDate").GetString())
            .ShouldBe(("2026-11-09", "2026-11-10", "2026-11-13"));
        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(3.74m);
    }

    [Test]
    public async Task ExampleD_NoTaxes_BooksOnlyThePrincipal()
    {
        var world = await WorldAsync("NZD", "NZD");

        var id = await CustodialHttp.ReadIdAsync(
            await PostAsync(world.Token, Body(world, 50m, 12.50m, "NewZealand", taxes: Array.Empty<object>())));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        Legs(item.RootElement).ShouldBe([("Principal", 12.50m, null)]);
        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(12.50m);
    }

    [Test]
    public async Task ABankAccount_AndADisabledInstrument_AreAccepted_AndNoHoldingIsTouched()
    {
        var world = await WorldAsync("NZD", "NZD", accountType: "Bank");
        await DisableInstrumentAsync(world.InstrumentId);

        await CustodialHttp.ReadIdAsync(await PostAsync(world.Token, ExampleA(world)));

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == world.AccountId);
        (await db.Holdings.IgnoreQueryFilters().CountAsync(row => row.AccountId == account.Id)).ShouldBe(0);
    }

    [Test]
    public async Task AnAdviser_RecordsADividend_ForAnAssignedCustomer()
    {
        var world = await WorldAsync("NZD", "NZD");
        var adviser = await TestApp.IssueAccessTokenAsync(world.Adviser);

        await CustodialHttp.ReadIdAsync(await PostAsync(adviser.AccessToken, ExampleA(world)));
    }

    [Test]
    public async Task ACustodialAccount_Returns400OnAccountId_AndWritesNothing()
    {
        var world = await WorldAsync("NZD", "NZD");
        var custodial = await CustodialHttp.OpenCustodialAsync(world.Token, world.Customer.PublicId, currency: "NZD");
        var body = ExampleA(world);
        body["accountId"] = custodial;
        body.Remove("bookedAt");
        var key = Guid.NewGuid();

        var errors = await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, body, key));

        errors["AccountId"].ShouldBe(["Dividends are paid by the platform from the company's announcement."]);
        (await CustodialHttp.TransactionCountAsync(custodial)).ShouldBe(0);
        (await CustodialHttp.IdempotencyRecordExistsAsync(key)).ShouldBeFalse();
    }

    [TestCase("type", "Dividend")]
    [TestCase("amount", 10)]
    [TestCase("currency", "NZD")]
    [TestCase("fee", 1)]
    [TestCase("reinvestedQuantity", 1)]
    [TestCase("ratio", 1)]
    public async Task AFieldTheBodyDoesNotHave_Returns400(string field, object value)
    {
        var world = await WorldAsync("NZD", "NZD");
        var body = ExampleA(world);
        body[field] = value;

        (await PostAsync(world.Token, body)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await AssertNothingWrittenAsync(world);
    }

    [Test]
    public async Task CashOrSecurityLegs_Return400()
    {
        var world = await WorldAsync("NZD", "NZD");
        var withCash = ExampleA(world);
        withCash["cash"] = new[] { new { accountId = world.AccountId, amount = 1m } };
        var withSecurity = ExampleA(world);
        withSecurity["security"] = new[] { new { instrumentId = world.InstrumentId, quantity = 1m } };

        (await PostAsync(world.Token, withCash)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(world.Token, withSecurity)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await AssertNothingWrittenAsync(world);
    }

    private static IEnumerable<TestCaseData> FieldRefusals()
    {
        TestCaseData Case(string name, string key, Action<Dictionary<string, object?>> change) =>
            new TestCaseData(key, change).SetName($"AFieldOutsideItsShape_Returns400AtTheField({name})");

        yield return Case("no accountId", "AccountId", body => body.Remove("accountId"));
        yield return Case("no instrumentId", "InstrumentId", body => body.Remove("instrumentId"));
        yield return Case("no quantity", "Quantity", body => body.Remove("quantity"));
        yield return Case("quantity 0", "Quantity", body => body["quantity"] = 0m);
        yield return Case("quantity 9 places", "Quantity", body => body["quantity"] = 1.123456789m);
        yield return Case("no grossAmount", "GrossAmount", body => body.Remove("grossAmount"));
        yield return Case("grossAmount 0", "GrossAmount", body => body["grossAmount"] = 0m);
        yield return Case("amountPerShare 0", "AmountPerShare", body => body["amountPerShare"] = 0m);
        yield return Case("amountPerShare too big", "AmountPerShare", body => body["amountPerShare"] = 1_000_000.01m);
        yield return Case("amountPerShare 9 places", "AmountPerShare", body => body["amountPerShare"] = 0.123456789m);
        yield return Case("no taxSource", "TaxSource", body => body.Remove("taxSource"));
        yield return Case("taxSource lower case", "TaxSource", body => body["taxSource"] = "newzealand");
        yield return Case("taxSource unknown", "TaxSource", body => body["taxSource"] = "Australia");
        yield return Case("credit negative", "ImputationCredit", body => body["imputationCredit"] = -1m);
        yield return Case("credit above gross", "ImputationCredit", body => body["imputationCredit"] = 2000.01m);
        yield return Case("kind lower case", "Taxes[0].Kind", body => body["taxes"] = Taxes(("rwt", 33m, 1m)));
        yield return Case("kind missing", "Taxes[0].Kind", body => body["taxes"] = new[] { new { ratePercent = 33m, amount = 1m } });
        yield return Case("rate 0", "Taxes[0].RatePercent", body => body["taxes"] = Taxes(("Rwt", 0m, 1m)));
        yield return Case("rate above 100", "Taxes[0].RatePercent", body => body["taxes"] = Taxes(("Rwt", 100.01m, 1m)));
        yield return Case("rate 3 places", "Taxes[0].RatePercent", body => body["taxes"] = Taxes(("Rwt", 33.333m, 1m)));
        yield return Case("rate missing", "Taxes[0].RatePercent", body => body["taxes"] = new[] { new { kind = "Rwt", amount = 1m } });
        yield return Case("amount 0", "Taxes[1].Amount", body => body["taxes"] = Taxes(("Rwt", 33m, 1m), ("Nrwt", 30m, 0m)));
        yield return Case("four taxes", "Taxes",
            body => body["taxes"] = Taxes(("Rwt", 33m, 1m), ("Rwt", 33m, 1m), ("Rwt", 33m, 1m), ("Rwt", 33m, 1m)));
        yield return Case("fxRate 0", "FxRate", body => body["fxRate"] = 0m);
        yield return Case("fxRate 9 places", "FxRate", body => body["fxRate"] = 1.123456789m);
        yield return Case("no bookedAt", "BookedAt", body => body.Remove("bookedAt"));
        yield return Case("bookedAt in the future", "BookedAt", body => body["bookedAt"] = "2099-01-01T00:00:00Z");
        yield return Case("memo blank", "Memo", body => body["memo"] = "  ");
        yield return Case("reference too long", "Reference", body => body["reference"] = new string('r', 101));
    }

    [TestCaseSource(nameof(FieldRefusals))]
    public async Task AFieldOutsideItsShape_Returns400AtTheField(string key, Action<Dictionary<string, object?>> change)
    {
        var world = await WorldAsync("NZD", "NZD");
        var body = ExampleA(world);
        change(body);
        var idempotency = Guid.NewGuid();

        var errors = await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, body, idempotency));

        errors.ShouldContainKey(key);
        await AssertNothingWrittenAsync(world, idempotency);
    }

    [Test]
    public async Task TheTaxSourceRules_HaveTheirMessages()
    {
        var world = await WorldAsync("NZD", "NZD");

        var foreignCredit = Body(world, 10m, 10.00m, "Foreign", imputationCredit: 1.00m);
        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, foreignCredit)))["ImputationCredit"]
            .ShouldBe(["Only a New Zealand company attaches imputation credits."]);

        var foreignTaxOnANewZealandCompany = Body(world, 10m, 10.00m, "NewZealand", Taxes(("ForeignWithholding", 15m, 1.50m)));
        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, foreignTaxOnANewZealandCompany)))["Taxes[0].Kind"]
            .ShouldBe(["Foreign tax is withheld from a foreign company's dividend only."]);

        var nrwtOnAForeignCompany = Body(world, 10m, 10.00m, "Foreign", Taxes(("Rwt", 33m, 1m), ("Nrwt", 30m, 3.00m)));
        var errors = await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, nrwtOnAForeignCompany));
        errors["Taxes"].ShouldBe(["RWT and NRWT cannot both be withheld from one payment."]);

        var nrwtAlone = Body(world, 10m, 10.00m, "Foreign", Taxes(("Nrwt", 30m, 3.00m)));
        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, nrwtAlone)))["Taxes[0].Kind"]
            .ShouldBe(["NRWT is withheld from a New Zealand company's dividend only."]);

        var twice = Body(world, 10m, 10.00m, "NewZealand", Taxes(("Rwt", 33m, 1m), ("Rwt", 10.5m, 1m)));
        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, twice)))["Taxes"]
            .ShouldBe(["Each tax kind can be entered once."]);

        var aboveGross = Body(world, 10m, 10.00m, "NewZealand", Taxes(("Rwt", 33m, 10.01m)));
        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, aboveGross)))["Taxes"]
            .ShouldBe(["Tax withheld cannot be more than the gross amount."]);
        await AssertNothingWrittenAsync(world);
    }

    [Test]
    public async Task MoreDecimalPlacesThanTheQuoteCurrency_Returns400AtTheField()
    {
        var world = await WorldAsync("NZD", "NZD");

        var gross = Body(world, 10m, 10.005m, "NewZealand");
        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, gross)))["GrossAmount"]
            .ShouldBe(["GrossAmount can have at most 2 decimal places."]);
        var credit = Body(world, 10m, 10.00m, "NewZealand", imputationCredit: 1.005m);
        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, credit)))["ImputationCredit"]
            .ShouldBe(["ImputationCredit can have at most 2 decimal places."]);
        var tax = Body(world, 10m, 10.00m, "NewZealand", Taxes(("Rwt", 33m, 3.301m)));
        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, tax)))["Taxes[0].Amount"]
            .ShouldBe(["Amount can have at most 2 decimal places."]);
        await AssertNothingWrittenAsync(world);
    }

    [Test]
    public async Task FxRate_IsRequiredAcrossCurrencies_AndRefusedWithinOne()
    {
        var across = await WorldAsync("NZD", "USD");
        var without = ExampleC(across);
        without.Remove("fxRate");
        (await CustodialHttp.ErrorsAsync(await PostAsync(across.Token, without)))["FxRate"]
            .ShouldBe(["FxRate is required when the instrument currency differs from the account currency."]);
        await AssertNothingWrittenAsync(across);

        var within = await WorldAsync("NZD", "NZD");
        var with = ExampleA(within);
        with["fxRate"] = 1.5m;
        (await CustodialHttp.ErrorsAsync(await PostAsync(within.Token, with)))["FxRate"]
            .ShouldBe(["FxRate cannot be set when the instrument currency is the account currency."]);
        await AssertNothingWrittenAsync(within);
    }

    [Test]
    public async Task AGrossThatConvertsTo0_OrTaxesAboveTheGrossAfterTheConversion_Return400()
    {
        var world = await WorldAsync("NZD", "USD");

        var tiny = Body(world, 1m, 0.01m, "Foreign");
        tiny["fxRate"] = 0.4m;
        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, tiny))).ShouldContainKey("GrossAmount");

        // 0.01 × 1.5 = 0.015 → 0.02 each, and the gross 0.02 × 1.5 = 0.03.
        var rounded = Body(world, 1m, 0.02m, "Foreign", Taxes(("ForeignWithholding", 50m, 0.01m), ("Rwt", 50m, 0.01m)));
        rounded["fxRate"] = 1.5m;
        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, rounded)))["Taxes"]
            .ShouldBe(["Tax withheld cannot be more than the gross amount."]);
        await AssertNothingWrittenAsync(world);
    }

    [Test]
    public async Task AClosedAccount_Returns400_AndADisabledCustomer_Returns400TargetUser()
    {
        var world = await WorldAsync("NZD", "NZD");
        await LegAccountsFixture.DisableCustomerAsync(world.Customer.Id);

        var disabled = await PostAsync(world.Token, ExampleA(world));
        disabled.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var json = JsonDocument.Parse(await disabled.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
            json.RootElement.GetProperty("target").GetString().ShouldBe("user");
        }

        var other = await WorldAsync("NZD", "NZD", tenantCode: "east-advisory");
        await LegAccountsFixture.CloseAccountDirectlyAsync(other.AccountId);
        (await PostAsync(other.Token, ExampleA(other))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await AssertNothingWrittenAsync(world);
        await AssertNothingWrittenAsync(other);
    }

    [Test]
    public async Task ADisabledTenant_Returns403ForItsUsers_And400TargetTenantForTheSystemAdmin()
    {
        var world = await WorldAsync("NZD", "NZD");
        await DisableTenantAsync(world.TenantId);

        (await PostAsync(world.Token, ExampleA(world))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var system = await AccountHttp.SignInSystemAdmin();
        var body = ExampleA(world);
        body["tenantId"] = world.TenantPublicId;
        var response = await PostAsync(system.AccessToken, body);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("tenant");
    }

    [Test]
    public async Task TenantAdminOmitsTenantId_SystemAdminMustSendIt()
    {
        var world = await WorldAsync("NZD", "NZD");
        var withTenant = ExampleA(world);
        withTenant["tenantId"] = world.TenantPublicId;

        (await CustodialHttp.ErrorsAsync(await PostAsync(world.Token, withTenant))).ShouldContainKey("TenantId");
        var system = await AccountHttp.SignInSystemAdmin();
        (await CustodialHttp.ErrorsAsync(await PostAsync(system.AccessToken, ExampleA(world)))).ShouldContainKey("TenantId");
        var unknown = ExampleA(world);
        unknown["tenantId"] = Guid.NewGuid();
        (await PostAsync(system.AccessToken, unknown)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await CustodialHttp.ReadIdAsync(await PostAsync(system.AccessToken, withTenant));
    }

    [Test]
    public async Task IdempotencyKey_IsRequired_ReplaysTheId_AndRefusesAnotherBody()
    {
        var world = await WorldAsync("NZD", "NZD");

        var missing = await TransactionHttp.Send(HttpMethod.Post, DividendPath, world.Token, ExampleA(world));
        (await CustodialHttp.ErrorsAsync(missing)).ShouldContainKey("IdempotencyKey");

        var key = Guid.NewGuid();
        var first = await CustodialHttp.ReadIdAsync(await PostAsync(world.Token, ExampleA(world), key));
        var replay = await CustodialHttp.ReadIdAsync(await PostAsync(world.Token, ExampleA(world), key));
        replay.ShouldBe(first);

        var changed = ExampleA(world);
        changed["memo"] = "Another dividend";
        (await PostAsync(world.Token, changed, key)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var otherTaxes = ExampleA(world);
        otherTaxes["taxes"] = Taxes(("Rwt", 33m, 525.00m));
        (await PostAsync(world.Token, otherTaxes, key)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(1);
    }

    [Test]
    public async Task Reverse_TakesTheNetBackOut_AndIsRefusedWhenTheCashWasSpent()
    {
        var world = await WorldAsync("NZD", "NZD");
        var first = await CustodialHttp.ReadIdAsync(await PostAsync(world.Token, ExampleA(world)));

        var reversalId = await CustodialHttp.ReadIdAsync(await CustodialHttp.ReverseAsync(world.Token, first));

        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(0m);
        using (var reversal = await CustodialHttp.GetTransactionAsync(world.Token, reversalId))
        {
            Legs(reversal.RootElement).ShouldBe([("Principal", -2000.00m, null), ("Tax", 526.00m, "Rwt")], ignoreOrder: true);
            reversal.RootElement.GetProperty("dividend").GetProperty("imputationCredit").GetDecimal().ShouldBe(200.00m);
        }

        var second = await CustodialHttp.ReadIdAsync(await PostAsync(world.Token, ExampleA(world)));
        await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(
            world.Token, CustodialHttp.Cash(world.AccountId, "TransferOut", -1000m).WithBookedAt(BookedAt)));
        (await CustodialHttp.ReverseAsync(world.Token, second)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task TheCustomer_ReadsTheDividend_InTheListAndTheItem()
    {
        var world = await WorldAsync("NZD", "USD", symbol: "AAPL");
        var id = await CustodialHttp.ReadIdAsync(await PostAsync(world.Token, ExampleC(world)));
        var customer = await TestApp.IssueAccessTokenAsync(world.Customer);

        var list = await TransactionHttp.Send(
            HttpMethod.Get, $"/transactions?accountId={world.AccountId}&type=Dividend", customer.AccessToken);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var row = json.RootElement.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        row.GetProperty("id").GetGuid().ShouldBe(id);
        row.GetProperty("dividend").GetProperty("quantity").GetDecimal().ShouldBe(10m);
        Tax(row, "ForeignWithholding").ShouldBe((15m, 4.40m, 0.66m, "NZD"));
        using var item = await CustodialHttp.GetTransactionAsync(customer.AccessToken, id);
        item.RootElement.GetProperty("fx").GetProperty("rate").GetDecimal().ShouldBe(1.69147m);
    }

    [Test]
    public async Task TheCustomerTransactionsReport_ShowsTheNet_AndTheTaxColumn()
    {
        var world = await WorldAsync("NZD", "NZD");
        await CustodialHttp.ReadIdAsync(await PostAsync(world.Token, ExampleA(world)));
        var system = await TenantHttp.SignInSystemAdmin();
        (await TenantHttp.Send(HttpMethod.Post, $"/tenants/{world.TenantPublicId}/reports", system.AccessToken,
            new { reportId = CustomerTransactionsId })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var text = await CustomerTransactionsTextAsync(world.Token, world.TenantPublicId, world.Customer.PublicId);

        text.ShouldContain("Tax");
        text.ShouldContain("1474.00 NZD");
        text.ShouldContain("-526.00 NZD");
    }

    [TestCase("External")]
    [TestCase("Custodial")]
    public async Task PostTransactions_WithTypeDividend_Returns400OnTypeWithTheMessageOfTheRoute(string custodyMode)
    {
        var world = await WorldAsync("NZD", "NZD");
        var accountId = custodyMode == "External"
            ? world.AccountId
            : await CustodialHttp.OpenCustodialAsync(world.Token, world.Customer.PublicId, currency: "NZD");
        var body = CustodialHttp.Cash(accountId, "Dividend", 10m);
        if (custodyMode == "External")
        {
            body.WithBookedAt(BookedAt);
        }

        var errors = await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(world.Token, body));

        errors["Type"].ShouldBe([
            "Type must be TransferIn, TransferOut, Interest, CloseOut, Opening, OpeningHoldings, Buy, Sell, StockIn, StockOut, Split, Bonus, or Scrip."
        ]);
        (await CustodialHttp.TransactionCountAsync(accountId)).ShouldBe(0);
    }

    internal sealed record World(
        int TenantId,
        Guid TenantPublicId,
        string Token,
        User Adviser,
        User Customer,
        Guid AccountId,
        Guid InstrumentId);

    /// <summary>A TenantAdmin with an External account and an instrument with no stored price. The account has no
    /// cash: a dividend only adds to it.</summary>
    internal static async Task<World> WorldAsync(
        string accountCurrency,
        string quoteCurrency,
        string symbol = "AIA",
        string accountType = "Brokerage",
        string tenantCode = "north-advisory")
    {
        var (tenant, adviser, customer, admin) = await AccountHttp.SignInTenantAdmin(
            tenantName: tenantCode, tenantCode: tenantCode, email: $"tess@{tenantCode}.example",
            adviserEmail: $"ann@{tenantCode}.example", customerEmail: $"ned@{tenantCode}.example");
        var accountId = await AccountHttp.OpenAccountAsync(
            admin.AccessToken, customer.PublicId, "Held elsewhere", accountType, accountCurrency);
        var instrumentId = await CustodialHttp.CreateInstrumentAsync(admin.AccessToken, symbol, quoteCurrency, price: null);
        return new World(tenant.Id, tenant.PublicId, admin.AccessToken, adviser, customer, accountId, instrumentId);
    }

    internal static Dictionary<string, object?> Body(
        World world,
        decimal quantity,
        decimal grossAmount,
        string taxSource,
        object? taxes = null,
        decimal? imputationCredit = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["accountId"] = world.AccountId,
            ["instrumentId"] = world.InstrumentId,
            ["quantity"] = quantity,
            ["grossAmount"] = grossAmount,
            ["taxSource"] = taxSource,
            ["bookedAt"] = BookedAt
        };
        if (taxes is not null)
        {
            body["taxes"] = taxes;
        }

        if (imputationCredit is not null)
        {
            body["imputationCredit"] = imputationCredit;
        }

        return body;
    }

    /// <summary>§8.1: a New Zealand company's dividend with nothing optional but the tax and the credit.</summary>
    internal static Dictionary<string, object?> ExampleA(World world) =>
        Body(world, 1000m, 2000.00m, "NewZealand", Taxes(("Rwt", 33m, 526.00m)), 200.00m);

    /// <summary>§8.1: a US share's dividend paid into an NZD account, with every optional field.</summary>
    internal static Dictionary<string, object?> ExampleC(World world)
    {
        var body = Body(world, 10m, 2.60m, "Foreign", Taxes(("ForeignWithholding", 15m, 0.39m)), 0m);
        body["amountPerShare"] = 0.26m;
        body["fxRate"] = 1.69147m;
        body["exDate"] = "2026-11-09";
        body["recordDate"] = "2026-11-10";
        body["paymentDate"] = "2026-11-13";
        body["memo"] = "Quarterly dividend";
        body["reference"] = "HATCH-2026-11";
        return body;
    }

    internal static object[] Taxes(params (string Kind, decimal RatePercent, decimal Amount)[] taxes) =>
        taxes.Select(tax => (object)new { kind = tax.Kind, ratePercent = tax.RatePercent, amount = tax.Amount }).ToArray();

    internal static Task<HttpResponseMessage> PostAsync(string token, object body, Guid? key = null) =>
        TransactionHttp.Send(HttpMethod.Post, DividendPath, token, body, key ?? Guid.NewGuid());

    internal static List<(string? Role, decimal Amount, string? Kind)> Legs(JsonElement item) =>
        TransactionJson.CashLegs(item).Select(leg => (
            leg.GetProperty("role").GetString(),
            leg.GetProperty("amount").GetDecimal(),
            leg.GetProperty("tax").ValueKind == JsonValueKind.Null
                ? null
                : leg.GetProperty("tax").GetProperty("kind").GetString())).ToList();

    internal static (decimal Rate, decimal Basis, decimal Amount, string? Currency) Tax(JsonElement item, string kind)
    {
        var tax = TransactionJson.CashLegs(item)
            .Select(leg => leg.GetProperty("tax"))
            .Single(tax => tax.ValueKind != JsonValueKind.Null && tax.GetProperty("kind").GetString() == kind);
        return (tax.GetProperty("ratePercent").GetDecimal(), tax.GetProperty("basisAmount").GetDecimal(),
            tax.GetProperty("amount").GetDecimal(), tax.GetProperty("currency").GetString());
    }

    /// <summary>Requests the Customer Transactions Report of the Customer, runs it and returns its text.</summary>
    internal static async Task<string> CustomerTransactionsTextAsync(string token, Guid tenantPublicId, Guid customerId)
    {
        var created = await TenantHttp.Send(HttpMethod.Post, "/reports/request", token, new
        {
            reportId = CustomerTransactionsId,
            parameters = new { customerId, from = "2026-01-01", to = "2099-01-01" }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Accepted, await created.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var executionId = json.RootElement.GetProperty("id").GetGuid();
        await using (var scope = FunctionalTestSetup.WebApiServices.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportJob>().Execute(executionId);
        }

        var file = Path.Combine(ReportStoragePaths.Root, tenantPublicId.ToString("D"), $"{executionId:D}.pdf");
        using var document = PdfDocument.Open(await File.ReadAllBytesAsync(file));
        return string.Join('\n', document.GetPages().Select(page => page.Text));
    }

    private static async Task AssertNothingWrittenAsync(World world, Guid? key = null)
    {
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(0);
        if (key is not null)
        {
            (await CustodialHttp.IdempotencyRecordExistsAsync(key.Value)).ShouldBeFalse();
        }
    }

    private static async Task DisableInstrumentAsync(Guid instrumentId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentId);
        instrument.Disable();
        await db.SaveChangesAsync();
    }

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
    }
}
