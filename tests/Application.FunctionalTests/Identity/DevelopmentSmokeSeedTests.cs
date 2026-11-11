using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Identity;

public class DevelopmentSmokeSeedTests : TestBase
{
    private const string AdviserPassword = "Smoke-Adviser-Password-For-Tests-0001!";
    private const string CustomerPassword = "Smoke-Customer-Password-For-Tests-0001!";

    [Test]
    public async Task Seed_SmokeAdviserSignsIn_AndSeesOnlySmokeCash()
    {
        await SeedAsync(AdviserPassword, CustomerPassword);
        (await SmokeTenantExistsAsync()).ShouldBeTrue();

        var adviser = await FunctionalTestSetup.Oidc.SignInAsync(
            DevelopmentSmokeSeeder.AdviserEmail, AdviserPassword, DevelopmentSmokeSeeder.TenantCode);
        adviser.AccessToken.ShouldNotBeNullOrWhiteSpace();

        var accounts = await AccountHttp.Send(HttpMethod.Get, "/accounts", adviser.AccessToken);
        accounts.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await accounts.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(1);
        items[0].GetProperty("name").GetString().ShouldBe(DevelopmentSmokeSeeder.AccountName);
        items[0].GetProperty("currency").GetString().ShouldBe("NZD");
    }

    [Test]
    public async Task Seed_SmokeCustomer_HasThePasswordAndOwnsSmokeCashWithOneTransferIn()
    {
        await SeedAsync(AdviserPassword, CustomerPassword);

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var tenant = await db.Tenants.SingleOrDefaultAsync(row => row.Code == DevelopmentSmokeSeeder.TenantCode);
        tenant.ShouldNotBeNull();
        tenant.Name.ShouldBe(DevelopmentSmokeSeeder.TenantName);

        var adviser = await db.DomainUsers.SingleAsync(
            row => row.TenantId == tenant.Id && row.Email == DevelopmentSmokeSeeder.AdviserEmail);
        adviser.Role.ShouldBe(UserRole.Adviser);

        var customer = await db.DomainUsers.SingleAsync(
            row => row.TenantId == tenant.Id && row.Email == DevelopmentSmokeSeeder.CustomerEmail);
        customer.Role.ShouldBe(UserRole.Customer);
        customer.AdviserId.ShouldBe(adviser.Id);

        var login = await userManager.FindByIdAsync(customer.IdentityUserId);
        login.ShouldNotBeNull();
        (await userManager.CheckPasswordAsync(login, CustomerPassword)).ShouldBeTrue();

        var account = await db.Accounts.SingleAsync(row => row.CustomerId == customer.Id);
        account.Name.ShouldBe(DevelopmentSmokeSeeder.AccountName);
        account.Type.ShouldBe(AccountType.Bank);

        var legs = await db.Transactions
            .Where(row => row.TenantId == tenant.Id)
            .SelectMany(row => row.CashLegs)
            .ToListAsync();
        legs.Count.ShouldBe(1);
        legs[0].AccountId.ShouldBe(account.Id);
        legs[0].Amount.ShouldBe(1000m);
    }

    [Test]
    public async Task Seed_Twice_KeepsOneAccountAndOneTransaction()
    {
        await SeedAsync(AdviserPassword, CustomerPassword);
        await SeedAsync(AdviserPassword, CustomerPassword);

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleOrDefaultAsync(row => row.Code == DevelopmentSmokeSeeder.TenantCode);
        tenant.ShouldNotBeNull();
        (await db.DomainUsers.CountAsync(row => row.TenantId == tenant.Id)).ShouldBe(2);
        (await db.Accounts.CountAsync(row => row.TenantId == tenant.Id)).ShouldBe(1);
        (await db.Transactions.CountAsync(row => row.TenantId == tenant.Id)).ShouldBe(1);
    }

    [TestCase(null, CustomerPassword)]
    [TestCase(AdviserPassword, null)]
    [TestCase("", "")]
    public async Task Seed_WithAMissingPassword_CreatesNoSmokeTenant(string? adviserPassword, string? customerPassword)
    {
        await SeedAsync(adviserPassword, customerPassword);

        (await SmokeTenantExistsAsync()).ShouldBeFalse();
    }

    private static async Task<bool> SmokeTenantExistsAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Tenants.AnyAsync(row => row.Code == DevelopmentSmokeSeeder.TenantCode);
    }

    private static async Task SeedAsync(string? adviserPassword, string? customerPassword)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        await DevelopmentSmokeSeeder.SeedAsync(
            scope.ServiceProvider, adviserPassword, customerPassword, CancellationToken.None);
    }
}
