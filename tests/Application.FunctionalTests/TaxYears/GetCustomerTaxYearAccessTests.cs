using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using static MyWealthV2.Application.FunctionalTests.TaxYears.TaxYearHttp;

namespace MyWealthV2.Application.FunctionalTests.TaxYears;

/// <summary>Tax-year-events R1: who reads a Customer's tax year, and two tenants.</summary>
[Parallelizable]
public class GetCustomerTaxYearAccessTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var response = await Send(null, Guid.NewGuid(), "2026");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task TheTenantAdmin_TheAssignedAdviser_TheCustomer_AndASystemAdmin_Read()
    {
        var world = await TaxYearWorld.CreateAsync();
        var adviser = await TestApp.IssueAccessTokenAsync(world.Adviser);
        var customer = await TestApp.IssueAccessTokenAsync(world.Customer);
        var systemAdmin = await AccountHttp.SignInSystemAdmin();

        foreach (var token in new[] { world.Token, adviser.AccessToken, customer.AccessToken, systemAdmin.AccessToken })
        {
            var year = await ReadAsync(token, world.Customer.PublicId, 2026);
            year.GetProperty("holdings").GetArrayLength().ShouldBe(1);
        }
    }

    [Test]
    public async Task AnUnassignedAdviser_AnotherCustomer_AndAMissingId_Return404()
    {
        var (tenant, _, customer, _) = await AccountHttp.SignInTenantAdmin();
        var otherAdviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Blair", "blair@north.example", "Password1!", tenant.Id);
        var otherCustomer = await TestApp.CreatePersonAsync(UserRole.Customer, "Pat", "pat@north.example", "Password1!", tenant.Id, otherAdviser.Id);
        var unassigned = await TestApp.IssueAccessTokenAsync(otherAdviser);
        var asCustomer = await TestApp.IssueAccessTokenAsync(customer);

        (await Send(unassigned.AccessToken, customer.PublicId, "2026")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(asCustomer.AccessToken, otherCustomer.PublicId, "2026")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(asCustomer.AccessToken, Guid.NewGuid(), "2026")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(unassigned.AccessToken, otherCustomer.PublicId, "2026")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task ADisabledCurrentTenant_Returns403()
    {
        var (tenant, _, customer, tokens) = await AccountHttp.SignInTenantAdmin();
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Tenants.SingleAsync(row => row.Id == tenant.Id);
            stored.Disable();
            await db.SaveChangesAsync();
        }

        (await Send(tokens.AccessToken, customer.PublicId, "2026")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task TwoTenants_TheOtherTenantsTenantAdminAndAdviser_Get404()
    {
        var north = await TaxYearWorld.CreateAsync();
        var (_, southAdviser, _, southTokens) = await AccountHttp.SignInTenantAdmin(
            "South Advisory", "south-advisory", "tess@south.example", adviserEmail: "ann@south.example", customerEmail: "ned@south.example");
        var southAdviserTokens = await TestApp.IssueAccessTokenAsync(southAdviser);

        (await Send(southTokens.AccessToken, north.Customer.PublicId, "2026")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(southAdviserTokens.AccessToken, north.Customer.PublicId, "2026")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TwoTenants_EachAnswerHoldsOnlyItsOwnTransactionsInstrumentsAndAccounts()
    {
        var north = await TaxYearWorld.CreateAsync();
        var south = await TaxYearWorld.CreateAsync("South Advisory", "south-advisory", "south.example");
        var systemAdmin = await AccountHttp.SignInSystemAdmin();

        foreach (var (world, token) in new[] { (north, north.Token), (south, south.Token), (south, systemAdmin.AccessToken) })
        {
            var other = world == north ? south : north;
            var year = await ReadAsync(token, world.Customer.PublicId, 2026);
            var text = year.GetRawText();

            HoldingOf(year, "AAA").GetProperty("instrument").GetProperty("id").GetGuid().ShouldBe(world.Aaa);
            foreach (var id in other.T.Values.Append(other.A1).Append(other.A2).Append(other.Aaa))
            {
                text.ShouldNotContain(id.ToString());
            }

            year.GetProperty("holdings").EnumerateArray().SelectMany(holding => holding.GetProperty("events").EnumerateArray())
                .Select(e => e.GetProperty("transactionId").GetGuid()).ShouldAllBe(id => world.T.Values.Contains(id));

            // Tax-cost-threshold: the totals hold the Customer's own purchases only, once.
            var threshold = year.GetProperty("threshold");
            (threshold.GetProperty("opening").GetDecimal(), threshold.GetProperty("closing").GetDecimal()).ShouldBe((12_355.70m, 10_792.33m));
            var steps = threshold.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("transactionId").GetGuid()).ToList();
            steps.ShouldNotBeEmpty();
            steps.ShouldAllBe(id => world.T.Values.Contains(id));

            // FIF income: the totals hold the Customer's own holding, prices and purchases only, once.
            var income = year.GetProperty("fifIncome");
            income.ValueKind.ShouldBe(JsonValueKind.Object);
            (income.GetProperty("fdr").GetProperty("openingValue").GetDecimal(), income.GetProperty("fdr").GetProperty("income").GetDecimal(),
                    income.GetProperty("cv").GetProperty("amount").GetDecimal(), income.GetProperty("complete").GetBoolean())
                .ShouldBe((13_108.48m, 655.42m, 1_740.87m, true));
        }
    }
}
