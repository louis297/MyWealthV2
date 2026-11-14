using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

/// <summary>
/// Two Customers in one tenant, each with one NZD Bank account. Ned is assigned to Ann, Nora to Bob.
/// No HTTP type writes legs on two Accounts yet, so two-Account Transactions are inserted directly.
/// </summary>
internal sealed record LegAccountsWorld(
    Tenant Tenant,
    User Ned,
    User Nora,
    Guid NedAccountId,
    Guid NoraAccountId,
    TokenResponse TenantAdmin,
    TokenResponse Ann,
    TokenResponse Bob);

internal static class LegAccountsFixture
{
    public static async Task<LegAccountsWorld> CreateAsync()
    {
        var (tenant, _, ned, tenantAdmin) = await AccountHttp.SignInTenantAdmin();
        var bob = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Bob Adviser", "bob@north.example", "Password1!", tenant.Id);
        var nora = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Nora North", "nora@north.example", "Password1!", tenant.Id, bob.Id);

        var nedAccountId = await OpenAccountAsync(tenantAdmin.AccessToken, ned.PublicId, "Ned bank");
        var noraAccountId = await OpenAccountAsync(tenantAdmin.AccessToken, nora.PublicId, "Nora bank");
        var annTokens = await FunctionalTestSetup.Oidc.SignInAsync("ann@north.example", "Password1!", "north-advisory");
        var bobTokens = await FunctionalTestSetup.Oidc.SignInAsync("bob@north.example", "Password1!", "north-advisory");

        return new LegAccountsWorld(
            tenant, ned, nora, nedAccountId, noraAccountId, tenantAdmin, annTokens, bobTokens);
    }

    /// <summary>Inserts a posted Transaction whose cash legs book into the given Accounts.</summary>
    public static async Task<Guid> InsertCashTransactionAsync(
        int tenantId,
        TransactionType type,
        params (Guid AccountId, decimal Amount)[] legs)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publicId = Guid.NewGuid();
        var bookedAt = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var typeValue = (int)type;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Transactions] ([PublicId], [TenantId], [Type], [BookedAt], [Created], [CreatedBy], [LastModified], [LastModifiedBy])
            VALUES ({publicId}, {tenantId}, {typeValue}, {bookedAt}, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'test')
            """);
        var transactionId = await db.Transactions
            .Where(row => row.PublicId == publicId)
            .Select(row => row.Id)
            .SingleAsync();

        foreach (var (accountPublicId, amount) in legs)
        {
            var account = await db.Accounts.SingleAsync(row => row.PublicId == accountPublicId);
            var currency = account.Currency;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [TransactionCashLegs] ([TenantId], [TransactionId], [AccountId], [Amount], [Currency], [Created], [CreatedBy], [LastModified], [LastModifiedBy])
                VALUES ({tenantId}, {transactionId}, {account.Id}, {amount}, {currency}, SYSDATETIMEOFFSET(), N'test', SYSDATETIMEOFFSET(), N'test')
                """);
        }

        return publicId;
    }

    /// <summary>Cash legs of a Transaction as (Account PublicId, Amount), ordered by leg id.</summary>
    public static async Task<List<(Guid AccountId, decimal Amount)>> CashLegsAsync(Guid transactionPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await (
            from transaction in db.Transactions
            join leg in db.TransactionCashLegs on transaction.Id equals leg.TransactionId
            join account in db.Accounts on leg.AccountId equals account.Id
            where transaction.PublicId == transactionPublicId
            orderby leg.Id
            select new { account.PublicId, leg.Amount }).ToListAsync();
        return rows.Select(row => (row.PublicId, row.Amount)).ToList();
    }

    public static async Task<int> CountTransactionsAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Transactions.CountAsync(row => row.TenantId == tenantId);
    }

    public static async Task CloseAccountDirectlyAsync(Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountPublicId);
        account.Close();
        await db.SaveChangesAsync();
    }

    public static async Task DisableCustomerAsync(int customerId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.DomainUsers.SingleAsync(row => row.Id == customerId);
        person.Disable();
        await db.SaveChangesAsync();
    }

    public static Task<HttpResponseMessage> ReverseAsync(string accessToken, Guid id) =>
        TransactionHttp.Send(HttpMethod.Post, $"/transactions/{id}/reverse", accessToken, body: null, Guid.NewGuid());

    public static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> OpenAccountAsync(string accessToken, Guid customerId, string name)
    {
        var response = await AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name,
            type = "Bank",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
