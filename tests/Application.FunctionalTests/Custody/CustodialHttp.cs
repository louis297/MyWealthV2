using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Custody;

/// <summary>Helpers for the custody modes functional tests (docs/features/custody-modes.md).</summary>
internal static class CustodialHttp
{
    public static Task<Guid> OpenCustodialAsync(
        string accessToken,
        Guid customerId,
        string name = "Managed portfolio",
        string currency = "USD") =>
        AccountHttp.OpenAccountAsync(accessToken, customerId, name, "Brokerage", currency, "Custodial");

    public static Task<Guid> OpenExternalAsync(
        string accessToken,
        Guid customerId,
        string name = "Held elsewhere",
        string type = "Brokerage",
        string currency = "USD") =>
        AccountHttp.OpenAccountAsync(accessToken, customerId, name, type, currency);

    /// <summary>
    /// Posts a body to a transaction create route as it is sent. A Custodial body carries no <c>bookedAt</c>
    /// (custody modes R10); a caller that posts to an External account adds it.
    /// </summary>
    public static Task<HttpResponseMessage> PostAsync(
        string accessToken,
        Dictionary<string, object?> body,
        string path = "/transactions",
        Guid? idempotencyKey = null) =>
        TransactionHttp.Send(HttpMethod.Post, path, accessToken, body, idempotencyKey ?? Guid.NewGuid());

    /// <summary>The body with a <c>bookedAt</c>, as an External account requires.</summary>
    public static Dictionary<string, object?> WithBookedAt(
        this Dictionary<string, object?> body,
        string bookedAt = "2026-10-11T10:00:00Z")
    {
        body["bookedAt"] = bookedAt;
        return body;
    }

    /// <summary>The body without its <c>bookedAt</c>, as a Custodial account requires.</summary>
    public static Dictionary<string, object?> WithoutBookedAt(this Dictionary<string, object?> body)
    {
        body.Remove("bookedAt");
        return body;
    }

    public static Dictionary<string, object?> Cash(Guid accountId, string type, decimal amount) => new()
    {
        ["accountId"] = accountId,
        ["type"] = type,
        ["amount"] = amount
    };

    public static Dictionary<string, object?> Trade(Guid accountId, string type, Guid instrumentId, decimal quantity) => new()
    {
        ["accountId"] = accountId,
        ["type"] = type,
        ["security"] = new[] { new { instrumentId, quantity } }
    };

    /// <summary>A <c>TransferIn</c>. Pass <paramref name="external"/> for an External account, which needs a bookedAt.</summary>
    public static Task<HttpResponseMessage> FundAsync(
        string accessToken,
        Guid accountId,
        decimal amount,
        bool external = false) =>
        PostAsync(accessToken, external
            ? Cash(accountId, "TransferIn", amount).WithBookedAt()
            : Cash(accountId, "TransferIn", amount));

    public static async Task<Guid> CreateInstrumentAsync(
        string accessToken,
        string symbol = "VTI",
        string quoteCurrency = "USD",
        decimal? price = 25.00m)
    {
        var response = await InstrumentHttp.Send(HttpMethod.Post, "/instruments", accessToken, new
        {
            symbol,
            name = symbol + " fund",
            quoteCurrency
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();
        if (price is not null)
        {
            using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await TestPrices.UpsertAsync(db, id, price.Value, quoteCurrency);
        }

        return id;
    }

    public static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    public static async Task<decimal> CashBalanceAsync(string accessToken, Guid accountId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/cash-balance", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("cashBalance").GetDecimal();
    }

    public static async Task<JsonDocument> GetTransactionAsync(string accessToken, Guid transactionId)
    {
        var response = await TransactionHttp.Send(HttpMethod.Get, $"/transactions/{transactionId}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        return JsonDocument.Parse(content);
    }

    public static Task<HttpResponseMessage> ReverseAsync(string accessToken, Guid transactionId) =>
        TransactionHttp.Send(
            HttpMethod.Post, $"/transactions/{transactionId}/reverse", accessToken, idempotencyKey: Guid.NewGuid());

    public static async Task<HttpResponseMessage> CloseAsync(string accessToken, Guid accountId)
    {
        var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
        return await AccountHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/close", accessToken, new { rowVersion });
    }

    public static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("code", out _).ShouldBeFalse(content);
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in json.RootElement.GetProperty("errors").EnumerateObject())
        {
            result[property.Name] = property.Value.EnumerateArray().Select(value => value.GetString()!).ToArray();
        }

        return result;
    }

    /// <summary>Transactions with a leg on the Account.</summary>
    public static async Task<int> TransactionCountAsync(Guid accountPublicId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.SingleAsync(row => row.PublicId == accountPublicId);
        return await db.Transactions.CountAsync(row =>
            row.CashLegs.Any(leg => leg.AccountId == account.Id)
            || row.SecurityLegs.Any(leg => leg.AccountId == account.Id));
    }

    public static async Task<bool> IdempotencyRecordExistsAsync(Guid key)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.IdempotencyRecords.AnyAsync(row => row.Key == key);
    }
}
