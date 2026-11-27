using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Tenants;
using static MyWealthV2.Application.FunctionalTests.Transactions.ExternalDividendTests;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>External income R9, R22–R24, R26, R27: a hand <c>Interest</c> on an External account may carry the taxes
/// an outside payer withheld (§1 examples E and F).</summary>
[Parallelizable]
public class ExternalInterestTaxTests : TestBase
{
    private const string BookedAt = "2026-11-26T00:00:00Z";
    private static readonly Guid CustomerTransactionsId = Guid.Parse("8c4e1a20-7b3d-4f6a-9e15-2d0c6b8a4f11");

    [Test]
    public async Task ExampleE_Rwt_IsATaxLegOnTheGross_AndTheItemHasNoInterestDetail()
    {
        var world = await WorldAsync("NZD", "NZD", accountType: "Bank");
        var body = Interest(world, 12.34m, Taxes(("Rwt", 33m, 4.07m)));
        body["memo"] = "November interest";

        var id = await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(world.Token, body));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        var root = item.RootElement;
        root.GetProperty("type").GetString().ShouldBe("Interest");
        root.GetProperty("source").GetProperty("kind").GetString().ShouldBe("Manual");
        root.GetProperty("interest").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("memo").GetString().ShouldBe("November interest");
        Legs(root).ShouldBe([("Principal", 12.34m, null), ("Tax", -4.07m, "Rwt")], ignoreOrder: true);
        Tax(root, "Rwt").ShouldBe((33m, 12.34m, 4.07m, "NZD"));
        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(8.27m);
    }

    [Test]
    public async Task ExampleF_ForeignWithholding_OnAnAudBankAccount()
    {
        var world = await WorldAsync("AUD", "AUD", accountType: "Bank");

        var id = await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(
            world.Token, Interest(world, 50.00m, Taxes(("ForeignWithholding", 10m, 5.00m)))));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        Legs(item.RootElement).ShouldBe(
            [("Principal", 50.00m, null), ("Tax", -5.00m, "ForeignWithholding")], ignoreOrder: true);
        Tax(item.RootElement, "ForeignWithholding").ShouldBe((10m, 50.00m, 5.00m, "AUD"));
        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(45.00m);
    }

    [Test]
    public async Task WithoutTaxes_OrWithAnEmptyList_AnInterestIsOneLegOfEitherSign()
    {
        var world = await WorldAsync("NZD", "NZD", accountType: "Bank");
        await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(world.Token, Interest(world, 10.00m, null)));

        var id = await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(
            world.Token, Interest(world, -2.00m, Array.Empty<object>())));

        using var item = await CustodialHttp.GetTransactionAsync(world.Token, id);
        Legs(item.RootElement).ShouldBe([("Principal", -2.00m, null)]);
        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(8.00m);
    }

    [Test]
    public async Task TaxesOnAnotherType_Returns400OnTaxes_AndAnEmptyListIsNoTaxes()
    {
        var world = await WorldAsync("NZD", "NZD", accountType: "Bank");
        var transferIn = CustodialHttp.Cash(world.AccountId, "TransferIn", 10m).WithBookedAt(BookedAt);
        transferIn["taxes"] = Taxes(("Rwt", 33m, 1m));
        var key = Guid.NewGuid();

        (await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(world.Token, transferIn, idempotencyKey: key)))["Taxes"]
            .ShouldBe(["Taxes can be set on Interest only."]);
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(0);
        (await CustodialHttp.IdempotencyRecordExistsAsync(key)).ShouldBeFalse();

        transferIn["taxes"] = Array.Empty<object>();
        await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(world.Token, transferIn));
    }

    [TestCase(0)]
    [TestCase(-12.34)]
    public async Task WithATax_TheAmountMustBeAbove0(decimal amount)
    {
        var world = await WorldAsync("NZD", "NZD", accountType: "Bank");

        var errors = await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(
            world.Token, Interest(world, amount, Taxes(("Rwt", 33m, 1m)))));

        errors["Amount"].ShouldContain("Interest with tax withheld needs a gross amount above 0.");
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }

    private static IEnumerable<TestCaseData> TaxRefusals()
    {
        TestCaseData Case(string name, string key, object taxes) =>
            new TestCaseData(key, taxes).SetName($"ATaxOutsideItsRules_Returns400AtTheField({name})");

        yield return Case("kind unknown", "Taxes[0].Kind", Taxes(("Foreign", 10m, 1m)));
        yield return Case("rate 0", "Taxes[0].RatePercent", Taxes(("Rwt", 0m, 1m)));
        yield return Case("amount 0", "Taxes[0].Amount", Taxes(("Rwt", 33m, 0m)));
        yield return Case("amount 3 places", "Taxes[0].Amount", Taxes(("Rwt", 33m, 4.071m)));
        yield return Case("a kind twice", "Taxes", Taxes(("Rwt", 33m, 1m), ("Rwt", 10.5m, 1m)));
        yield return Case("rwt with nrwt", "Taxes", Taxes(("Rwt", 33m, 1m), ("Nrwt", 15m, 1m)));
        yield return Case("above the gross", "Taxes", Taxes(("Rwt", 33m, 10m), ("ForeignWithholding", 10m, 2.35m)));
        yield return Case("four taxes", "Taxes",
            Taxes(("Rwt", 33m, 1m), ("Nrwt", 15m, 1m), ("ForeignWithholding", 10m, 1m), ("Rwt", 33m, 1m)));
    }

    [TestCaseSource(nameof(TaxRefusals))]
    public async Task ATaxOutsideItsRules_Returns400AtTheField(string key, object taxes)
    {
        var world = await WorldAsync("NZD", "NZD", accountType: "Bank");

        var errors = await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(world.Token, Interest(world, 12.34m, taxes)));

        errors.ShouldContainKey(key);
        (await CustodialHttp.TransactionCountAsync(world.AccountId)).ShouldBe(0);
    }

    [Test]
    public async Task OnACustodialAccount_AnInterestWithTaxes_IsStillRefusedByTheMatrix()
    {
        var world = await WorldAsync("NZD", "NZD");
        var custodial = await CustodialHttp.OpenCustodialAsync(world.Token, world.Customer.PublicId, currency: "NZD");
        var body = CustodialHttp.Cash(custodial, "Interest", 12.34m);
        body["taxes"] = Taxes(("Rwt", 33m, 4.07m));

        (await CustodialHttp.ErrorsAsync(await CustodialHttp.PostAsync(world.Token, body)))["Type"]
            .ShouldBe(["Interest cannot be entered by hand on a Custodial account."]);
        (await CustodialHttp.TransactionCountAsync(custodial)).ShouldBe(0);
    }

    [Test]
    public async Task Reverse_CopiesTheTaxLeg_AndTheTaxesAreInTheIdempotencyHash()
    {
        var world = await WorldAsync("NZD", "NZD", accountType: "Bank");
        var key = Guid.NewGuid();
        var id = await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(
            world.Token, Interest(world, 12.34m, Taxes(("Rwt", 33m, 4.07m))), idempotencyKey: key));

        (await CustodialHttp.PostAsync(world.Token, Interest(world, 12.34m, Taxes(("Rwt", 33m, 4.06m))), idempotencyKey: key))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(
            world.Token, Interest(world, 12.34m, Taxes(("Rwt", 33m, 4.07m))), idempotencyKey: key))).ShouldBe(id);

        var reversalId = await CustodialHttp.ReadIdAsync(await CustodialHttp.ReverseAsync(world.Token, id));

        using var reversal = await CustodialHttp.GetTransactionAsync(world.Token, reversalId);
        Legs(reversal.RootElement).ShouldBe([("Principal", -12.34m, null), ("Tax", 4.07m, "Rwt")], ignoreOrder: true);
        (await CustodialHttp.CashBalanceAsync(world.Token, world.AccountId)).ShouldBe(0m);
    }

    [Test]
    public async Task TheCustomer_ReadsTheTax_AndTheReportShowsTheNetAndTheTaxColumn()
    {
        var world = await WorldAsync("NZD", "NZD", accountType: "Bank");
        var id = await CustodialHttp.ReadIdAsync(await CustodialHttp.PostAsync(
            world.Token, Interest(world, 12.34m, Taxes(("Rwt", 33m, 4.07m)))));
        var customer = await TestApp.IssueAccessTokenAsync(world.Customer);

        using (var item = await CustodialHttp.GetTransactionAsync(customer.AccessToken, id))
        {
            Tax(item.RootElement, "Rwt").ShouldBe((33m, 12.34m, 4.07m, "NZD"));
        }

        var system = await TenantHttp.SignInSystemAdmin();
        (await TenantHttp.Send(HttpMethod.Post, $"/tenants/{world.TenantPublicId}/reports", system.AccessToken,
            new { reportId = CustomerTransactionsId })).StatusCode.ShouldBe(HttpStatusCode.Created);
        var text = await CustomerTransactionsTextAsync(world.Token, world.TenantPublicId, world.Customer.PublicId);
        text.ShouldContain("8.27 NZD");
        text.ShouldContain("-4.07 NZD");
    }

    private static Dictionary<string, object?> Interest(World world, decimal amount, object? taxes)
    {
        var body = CustodialHttp.Cash(world.AccountId, "Interest", amount).WithBookedAt(BookedAt);
        if (taxes is not null)
        {
            body["taxes"] = taxes;
        }

        return body;
    }
}
