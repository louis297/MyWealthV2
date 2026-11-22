using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.Fees;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Orders;

/// <summary>The <c>/orders</c> routes (docs/features/orders.md §8).</summary>
internal static class OrderHttp
{
    public const string Path = "/orders";
    public const string StaleRowVersion = "AAAAAAAAAAE=";

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null) =>
        AccountHttp.Send(method, path, accessToken, body);

    public static Dictionary<string, object?> Body(Guid accountId, Guid instrumentId, string side, decimal quantity) => new()
    {
        ["accountId"] = accountId,
        ["instrumentId"] = instrumentId,
        ["side"] = side,
        ["quantity"] = quantity
    };

    public static Task<HttpResponseMessage> PostAsync(string accessToken, Dictionary<string, object?> body, Guid? key = null) =>
        TransactionHttp.Send(HttpMethod.Post, Path, accessToken, body, key ?? Guid.NewGuid());

    /// <summary>Places an order and answers its id.</summary>
    public static async Task<Guid> PlaceAsync(string accessToken, Guid accountId, Guid instrumentId, string side, decimal quantity)
    {
        var response = await PostAsync(accessToken, Body(accountId, instrumentId, side, quantity));
        return await CustodialHttp.ReadIdAsync(response);
    }

    public static async Task<JsonElement> GetAsync(string accessToken, Guid id)
    {
        var response = await Send(HttpMethod.Get, $"{Path}/{id}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static async Task<List<JsonElement>> ListAsync(string accessToken, string query = "")
    {
        var response = await Send(HttpMethod.Get, Path + query, accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    public static async Task<List<Guid>> ListIdsAsync(string accessToken, string query = "") =>
        (await ListAsync(accessToken, query)).Select(item => item.GetProperty("id").GetGuid()).ToList();

    /// <summary>Posts a cancel with the order's current <c>rowVersion</c>.</summary>
    public static async Task<HttpResponseMessage> CancelAsync(string accessToken, Guid id, string? note = null)
    {
        var rowVersion = (await OrderAsync(id)).RowVersion;
        return await Send(HttpMethod.Post, $"{Path}/{id}/cancel", accessToken, new { note, rowVersion = Convert.ToBase64String(rowVersion) });
    }

    /// <summary>Records a send attempt on the order, as the send step does before it calls the broker (R20).</summary>
    public static async Task RecordAttemptAsync(Guid id)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.IgnoreQueryFilters().SingleAsync(row => row.PublicId == id);
        order.RecordAttempt(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }

    /// <summary>The two reads and place.</summary>
    public static IEnumerable<(HttpMethod Method, string Route, object? Body)> Routes(Guid id)
    {
        yield return (HttpMethod.Get, Path, null);
        yield return (HttpMethod.Get, $"{Path}/{id}", null);
        yield return (HttpMethod.Post, Path, Body(Guid.NewGuid(), Guid.NewGuid(), "Buy", 1m));
    }

    /// <summary>An instrument of the caller's tenant with a price stored for <paramref name="priceDate"/> (default
    /// today, so an order may use it, R10); no price when <paramref name="price"/> is null.</summary>
    public static async Task<Guid> CreateInstrumentAsync(
        string accessToken,
        string symbol = "ACME",
        string quoteCurrency = "USD",
        decimal? price = 100m,
        DateOnly? priceDate = null)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " Holdings",
            quoteCurrency
        });
        var id = await CustodialHttp.ReadIdAsync(response);
        if (price is not null)
        {
            await SetPriceAsync(id, price.Value, priceDate ?? Today);
        }

        return id;
    }

    /// <summary>Stores a price of the instrument for <paramref name="asOf"/>.</summary>
    public static async Task SetPriceAsync(Guid instrumentPublicId, decimal amount, DateOnly asOf)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentPublicId);
        var row = await db.InstrumentPrices.SingleOrDefaultAsync(price => price.InstrumentId == instrument.Id && price.AsOfDate == asOf);
        if (row is null)
        {
            row = new InstrumentPrice { InstrumentId = instrument.Id, AsOfDate = asOf };
            db.InstrumentPrices.Add(row);
        }

        row.Price = amount;
        row.Source = "EODHD";
        row.FetchedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>A holding on the account, from a <c>Buy</c> with source <c>Manual</c> written straight to the database
    /// at <paramref name="price"/>, as a trade typed before this slice. The cash it costs must be on the account.</summary>
    public static async Task<Guid> InsertHoldingAsync(Guid accountPublicId, Guid instrumentPublicId, decimal quantity, decimal price = 25m)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentPublicId);
        var cash = await db.TransactionCashLegs.IgnoreQueryFilters().Where(leg => leg.AccountId == account.Id).SumAsync(leg => leg.Amount);
        var quote = instrument.QuoteCurrency.Trim();
        var sameCurrency = string.Equals(quote, account.Currency.Trim(), StringComparison.Ordinal);
        var buy = Transaction.PostBuy(
            account.TenantId, account.Id, account.Type, account.Currency, instrument.Id, quantity, Money.Create(price, quote),
            fxRate: 1m, DateTimeOffset.UtcNow.AddMinutes(-5), memo: null, reference: null, TransactionSource.Manual,
            currentCashSum: cash,
            appliedRate: sameCurrency ? null : AppliedFxRate.Create(quote, account.Currency.Trim(), 1m, Today, "Test"));
        buy.CreatedBy = "test";
        buy.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        buy.SecurityLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(buy);
        var holding = Holding.Create(account.TenantId, account.Id, instrument.Id, quantity, buy.SecurityLegs.Single().CostAmount, quote);
        holding.CreatedBy = "test";
        db.Holdings.Add(holding);
        await db.SaveChangesAsync();
        return buy.PublicId;
    }

    /// <summary>Changes a holding under its orders, as a corporate action can (R50): an <c>Adjustment</c> written
    /// straight to the database, which the reserved quantity rule of the adjustment route would refuse.</summary>
    public static async Task ChangeHoldingAsync(Guid accountPublicId, Guid instrumentPublicId, decimal quantityChange, decimal costChange)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentPublicId);
        var holding = await db.Holdings.IgnoreQueryFilters().SingleAsync(row => row.AccountId == account.Id && row.InstrumentId == instrument.Id);
        var cash = await db.TransactionCashLegs.IgnoreQueryFilters().Where(leg => leg.AccountId == account.Id).SumAsync(leg => leg.Amount);
        var adjustment = Transaction.PostAdjustment(
            account.TenantId, account.Id, account.Type, account.Currency, Domain.Enums.AdjustmentReason.CorporateAction,
            "Holding changed for a test", null, DateTimeOffset.UtcNow, TransactionSource.Manual, null, cash,
            new AdjustmentHoldingChange(
                instrument.Id, instrument.QuoteCurrency.Trim(), quantityChange, costChange, holding.Quantity, holding.CostAmount));
        adjustment.CreatedBy = "test";
        adjustment.SecurityLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(adjustment);
        if (holding.ReplaceTotals(holding.Quantity + quantityChange, holding.CostAmount + costChange, holding.CostCurrency))
        {
            db.Holdings.Remove(holding);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Sets the status of an account straight in the database.</summary>
    public static async Task SetAccountStatusAsync(Guid accountPublicId, Domain.Enums.AccountStatus status)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Accounts] SET [Status] = {(int)status} WHERE [PublicId] = {accountPublicId}");
    }

    /// <summary>Sets or clears the broker connection of a tenant straight in the database.</summary>
    public static async Task SetBrokerConnectionAsync(int tenantId, string? connection)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Tenants] SET [BrokerConnection] = {connection} WHERE [Id] = {tenantId}");
    }

    /// <summary>Moves an order to another broker connection straight in the database.</summary>
    public static async Task SetConnectionAsync(Guid id, string connection)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Orders] SET [Connection] = {connection} WHERE [PublicId] = {id}");
    }

    public static async Task<Order> OrderAsync(Guid id)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Orders.IgnoreQueryFilters().AsNoTracking().Include(order => order.Fills).SingleAsync(order => order.PublicId == id);
    }

    public static async Task<int> OrderCountAsync(Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountPublicId);
        return await db.Orders.IgnoreQueryFilters().CountAsync(order => order.AccountId == account.Id);
    }
}

/// <summary>
/// The orders test world (orders §10): a tenant on broker connection <c>simulator</c> with two TenantAdmins, an
/// Adviser (Ann) with a Customer (Ned), a second Adviser (Bob) with a Customer (Olga). Ned has an Open Custodial USD
/// account funded with 5 000.00 through the fake bank feed. The instrument <c>ACME</c> is quoted in USD at 100.00
/// today, and the tenant's USD trade fee schedule is 0.5 % with a minimum of 5.00, as the example of spec §8.1.
/// </summary>
internal sealed record OrderWorld(NominatedWorld People, Guid AccountId, Guid InstrumentId)
{
    public Tenant Tenant => People.Tenant;
    public User Customer => People.Customer;
    public string TenantAdmin => People.TenantAdmin.AccessToken;
    public string Adviser => People.AdviserToken.AccessToken;
    public string CustomerToken => People.CustomerToken.AccessToken;
    public string OtherCustomerToken => People.OtherCustomerToken.AccessToken;

    public static async Task<OrderWorld> CreateAsync(
        string name = "North Advisory",
        string code = "north-advisory",
        decimal funding = 5000.00m,
        decimal? price = 100m,
        bool feeSchedule = true)
    {
        var people = await NominatedWorld.CreateAsync(name, code);
        var accountId = await CustodialHttp.OpenCustodialAsync(people.TenantAdmin.AccessToken, people.Customer.PublicId);
        if (funding > 0)
        {
            (await CustodialHttp.FundAsync(people.TenantAdmin.AccessToken, accountId, funding)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var instrumentId = await OrderHttp.CreateInstrumentAsync(people.TenantAdmin.AccessToken, price: price);
        if (feeSchedule)
        {
            await FeeHttp.AddScheduleAsync(people.Tenant.Id, "USD", new DateOnly(2026, 11, 1), 5.00m, new FeeBand(0m, 0.5m));
        }

        return new OrderWorld(people, accountId, instrumentId);
    }

    /// <summary>The Bob Adviser's token: an Adviser of the tenant who is not Ned's.</summary>
    public async Task<string> OtherAdviserAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bob = await db.DomainUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == People.OtherCustomer.AdviserId);
        return (await TestApp.IssueAccessTokenAsync(bob)).AccessToken;
    }

    public async Task<(decimal Cash, decimal Reserved, decimal Available)> CashAsync(Guid? accountId = null)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId ?? AccountId}/cash-balance", TenantAdmin);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        return (root.GetProperty("cashBalance").GetDecimal(), root.GetProperty("reservedCash").GetDecimal(),
            root.GetProperty("availableCash").GetDecimal());
    }

    /// <summary>Places an order as the Adviser.</summary>
    public Task<Guid> PlaceAsync(string side, decimal quantity, Guid? instrumentId = null, Guid? accountId = null) =>
        OrderHttp.PlaceAsync(Adviser, accountId ?? AccountId, instrumentId ?? InstrumentId, side, quantity);

    public Task<HttpResponseMessage> TryPlaceAsync(string side, decimal quantity, Guid? instrumentId = null, Guid? accountId = null) =>
        OrderHttp.PostAsync(Adviser, OrderHttp.Body(accountId ?? AccountId, instrumentId ?? InstrumentId, side, quantity));
}
