using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Portfolios;

/// <summary>
/// Portfolios amendment 2026-11-13 part 2 fixture. One tenant, Advisers A and B, Customer CA (Adviser A) and
/// Customer CB (Adviser B), each with a funded Open Brokerage account, and one active portfolio P created by A.
/// </summary>
internal sealed class AdviserBookWorld
{
    private const string Password = "Password1!";

    private AdviserBookWorld(
        TokenResponse adviserA,
        TokenResponse adviserB,
        TokenResponse tenantAdmin,
        Guid accountA,
        Guid accountB,
        Guid portfolioId,
        Guid firstInstrumentId,
        Guid secondInstrumentId)
    {
        AdviserA = adviserA;
        AdviserB = adviserB;
        TenantAdmin = tenantAdmin;
        AccountA = accountA;
        AccountB = accountB;
        PortfolioId = portfolioId;
        FirstInstrumentId = firstInstrumentId;
        SecondInstrumentId = secondInstrumentId;
    }

    public TokenResponse AdviserA { get; }

    public TokenResponse AdviserB { get; }

    public TokenResponse TenantAdmin { get; }

    /// <summary>CA's account, in Adviser A's Book.</summary>
    public Guid AccountA { get; }

    /// <summary>CB's account, outside Adviser A's Book.</summary>
    public Guid AccountB { get; }

    public Guid PortfolioId { get; }

    public Guid FirstInstrumentId { get; }

    public Guid SecondInstrumentId { get; }

    public static async Task<AdviserBookWorld> CreateAsync(string code)
    {
        var tenant = await TestApp.CreateTenantAsync(code, code);
        await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tess Admin", $"tess@{code}", Password, tenant.Id);
        var adviserA = await TestApp.CreatePersonAsync(UserRole.Adviser, "Ann Adviser", $"ann@{code}", Password, tenant.Id);
        var adviserB = await TestApp.CreatePersonAsync(UserRole.Adviser, "Bob Adviser", $"bob@{code}", Password, tenant.Id);
        var customerA = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cara Customer", $"cara@{code}", Password, tenant.Id, adviserA.Id);
        var customerB = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Cole Customer", $"cole@{code}", Password, tenant.Id, adviserB.Id);
        var adminTokens = await FunctionalTestSetup.Oidc.SignInAsync($"tess@{code}", Password, code);
        var adviserATokens = await FunctionalTestSetup.Oidc.SignInAsync($"ann@{code}", Password, code);
        var adviserBTokens = await FunctionalTestSetup.Oidc.SignInAsync($"bob@{code}", Password, code);

        var first = await CreateInstrumentAsync(adminTokens.AccessToken, "AIA");
        var second = await CreateInstrumentAsync(adminTokens.AccessToken, "FPH");
        var accountA = await CreateFundedAccountAsync(adviserATokens.AccessToken, customerA.PublicId);
        var accountB = await CreateFundedAccountAsync(adviserBTokens.AccessToken, customerB.PublicId);

        var created = await PortfolioHttp.Send(HttpMethod.Post, "/portfolios", adviserATokens.AccessToken, new
        {
            name = "Growth",
            baseCurrency = "NZD",
            cashResidualPercent = 0m,
            minimumTrade = 1m,
            lines = new[] { new { instrumentId = first, targetWeight = 100m } }
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return new AdviserBookWorld(
            adviserATokens,
            adviserBTokens,
            adminTokens,
            accountA,
            accountB,
            json.RootElement.GetProperty("id").GetGuid(),
            first,
            second);
    }

    public static RecordingRebalanceScheduler Scheduler =>
        FunctionalTestSetup.WebApiServices.GetRequiredService<RecordingRebalanceScheduler>();

    /// <summary>Assigns P to both accounts, each by its own Adviser.</summary>
    public async Task ShareAsync()
    {
        await AssignAsync(AdviserA, AccountA);
        await AssignAsync(AdviserB, AccountB);
    }

    public async Task AssignAsync(TokenResponse tokens, Guid accountId)
    {
        var response = await PortfolioHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/portfolio", tokens.AccessToken, new
        {
            portfolioId = PortfolioId
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public async Task<JsonElement> GetPortfolioAsync()
    {
        var response = await PortfolioHttp.Send(HttpMethod.Get, $"/portfolios/{PortfolioId}", TenantAdmin.AccessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public async Task<string> RowVersionAsync() => (await GetPortfolioAsync()).GetProperty("rowVersion").GetString()!;

    /// <summary>Lines 50 / 50 over both instruments; P is created with 100 on the first.</summary>
    public object[] NewLines() =>
    [
        new { instrumentId = FirstInstrumentId, targetWeight = 50m },
        new { instrumentId = SecondInstrumentId, targetWeight = 50m }
    ];

    public object[] SameLines() => [new { instrumentId = FirstInstrumentId, targetWeight = 100m }];

    public static IReadOnlyList<Guid> EnqueuedSince(int before) =>
        Scheduler.Enqueued.Skip(before).Select(item => item.AccountId).ToList();

    public static async Task<IReadOnlyList<Guid>> AcceptedAccountsAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accounts").EnumerateArray()
            .Select(item => item.GetProperty("accountId").GetGuid())
            .ToList();
    }

    private static async Task<Guid> CreateInstrumentAsync(string accessToken, string symbol)
    {
        var response = await Instruments.InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " fund",
            quoteCurrency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var instrumentId = json.RootElement.GetProperty("id").GetGuid();
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await TestPrices.UpsertAsync(db, instrumentId, 10m, "NZD");
        return instrumentId;
    }

    private static async Task<Guid> CreateFundedAccountAsync(string accessToken, Guid customerId)
    {
        var response = await Accounts.AccountHttp.Send(HttpMethod.Post, "/accounts", accessToken, new
        {
            customerId,
            name = "Model",
            type = "Brokerage",
            currency = "NZD"
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accountId = json.RootElement.GetProperty("id").GetGuid();
        (await Transactions.TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken, new
        {
            accountId,
            type = "TransferIn",
            amount = 10000m,
            bookedAt = "2026-01-02T00:00:00Z",
            currency = "NZD"
        }, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.Created);
        return accountId;
    }
}
