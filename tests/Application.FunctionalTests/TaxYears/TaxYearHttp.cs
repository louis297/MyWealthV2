using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Domain.Valuations;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.TaxYears;

/// <summary>Helpers for the tax year functional tests (docs/features/tax-year-events.md §10).</summary>
internal static class TaxYearHttp
{
    public static Task<HttpResponseMessage> Send(string? accessToken, Guid customerId, string taxYear) =>
        AccountHttp.Send(HttpMethod.Get, $"/users/customers/{customerId}/tax-years/{taxYear}", accessToken);

    /// <summary>The tax year, asserted 200.</summary>
    public static async Task<JsonElement> ReadAsync(string accessToken, Guid customerId, int taxYear)
    {
        var response = await Send(accessToken, customerId, taxYear.ToString());
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public static JsonElement HoldingOf(JsonElement year, string symbol) =>
        year.GetProperty("holdings").EnumerateArray().Single(holding =>
            holding.GetProperty("instrument").GetProperty("symbol").GetString() == symbol);

    public static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToList();

    /// <summary>Sets the issuer country and one FIF treatment row through <c>PUT /instruments/{id}/tax-attributes</c>.</summary>
    public static async Task ClassifyAsync(string accessToken, Guid instrumentId, string country, int taxYear, string treatment)
    {
        var item = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{instrumentId}", accessToken);
        using var json = JsonDocument.Parse(await item.Content.ReadAsStringAsync());
        var response = await InstrumentHttp.Send(HttpMethod.Put, $"/instruments/{instrumentId}/tax-attributes", accessToken, new
        {
            issuerCountry = country,
            fifTreatments = new[] { new { taxYear, treatment, note = (string?)null } },
            rowVersion = json.RootElement.GetProperty("rowVersion").GetString()
        });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Sets the issuer country and several FIF treatment rows through <c>PUT /instruments/{id}/tax-attributes</c>.</summary>
    public static async Task ClassifyAsync(string accessToken, Guid instrumentId, string country, params (int TaxYear, string Treatment)[] rows)
    {
        var item = await InstrumentHttp.Send(HttpMethod.Get, $"/instruments/{instrumentId}", accessToken);
        using var json = JsonDocument.Parse(await item.Content.ReadAsStringAsync());
        var response = await InstrumentHttp.Send(HttpMethod.Put, $"/instruments/{instrumentId}/tax-attributes", accessToken, new
        {
            issuerCountry = country,
            fifTreatments = rows.Select(row => new { taxYear = row.TaxYear, treatment = row.Treatment, note = (string?)null }).ToArray(),
            rowVersion = json.RootElement.GetProperty("rowVersion").GetString()
        });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The <c>StockIn</c> of an in-kind transfer in with its cost and <c>acquiredOn</c> (in-kind-transfers R37, R38), as
    /// the platform posts it on a Custodial account. Written with the Domain factory, because the server sets
    /// <c>bookedAt</c> there.
    /// </summary>
    public static async Task<Guid> PostTransferInAsync(
        Guid accountId, Guid instrumentId, decimal quantity, decimal cost, DateOnly? acquiredOn, DateTimeOffset bookedAt)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentId);
        var transferId = Guid.NewGuid();
        var origin = new InKindTransferOrigin(
            transferId, "Other Broker", "12345678", false, acquiredOn, LedgerDay.Of(bookedAt), "REF-" + transferId.ToString("N")[..8]);
        var posting = Transaction.PostTransferIn(
            account.TenantId, account.Id, account.Type, instrument.Id, quantity, cost, instrument.QuoteCurrency, bookedAt,
            TransactionSource.InKindTransfer(transferId), origin);
        posting.CreatedBy = "test";
        posting.SecurityLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(posting);
        await db.SaveChangesAsync();
        return posting.PublicId;
    }

    /// <summary>A security body for <c>POST /transactions</c> on an External account.</summary>
    public static Dictionary<string, object?> Security(
        Guid accountId, string type, Guid instrumentId, decimal quantity, string bookedAt, decimal? cost = null) => new()
    {
        ["accountId"] = accountId,
        ["type"] = type,
        ["bookedAt"] = bookedAt,
        ["security"] = cost is null
            ? new object[] { new { instrumentId, quantity } }
            : new object[] { new { instrumentId, quantity, cost } }
    };

    /// <summary>
    /// A dividend of a corporate action, as the platform posts it on a Custodial account: a tax source of
    /// <c>Foreign</c>, 15 % foreign tax and RWT at 33 %, with a reinvestment when a plan price is given. Written with the
    /// Domain factory, because a Custodial account's <c>bookedAt</c> is the server's.
    /// </summary>
    public static async Task<Guid> PostDividendAsync(
        Guid accountId,
        Guid instrumentId,
        decimal shares,
        decimal amountPerShare,
        DateTimeOffset bookedAt,
        decimal? reinvestmentPrice = null,
        decimal? reinvestedQuantity = null)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentId);
        var terms = DividendTerms.Create(amountPerShare, DividendTaxSource.Foreign, null, 15m, reinvestmentPrice);
        var day = LedgerDay.Of(bookedAt);
        var posting = Transaction.PostDividend(
            account.TenantId, account.Id, account.Currency, 2, instrument.Id, instrument.Symbol, instrument.QuoteCurrency, terms,
            day.AddDays(-5), null, day, shares, terms.SourceAmounts(shares, 2), new DividendTax(TaxKind.Rwt, 33m), reinvestedQuantity,
            null, bookedAt, TransactionSource.CorporateAction(Guid.NewGuid()));
        posting.CreatedBy = "test";
        posting.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        posting.SecurityLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(posting);
        await db.SaveChangesAsync();
        return posting.PublicId;
    }

    public static async Task CloseAccountAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        account.Close();
        await db.SaveChangesAsync();
    }
}
