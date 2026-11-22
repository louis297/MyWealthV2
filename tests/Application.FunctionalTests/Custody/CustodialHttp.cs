using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Instruments;
using MyWealthV2.Application.FunctionalTests.Transactions;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
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

    /// <summary>
    /// Funds an account. An External account gets a hand <c>TransferIn</c>, which needs a bookedAt. A Custodial
    /// account cannot (deposit-matching R35): the helper adds a receipt with the account's deposit reference to the
    /// functional host's fake bank feed, runs the pull and the processing, and answers 201 with the deposit's id
    /// as the create route would, or 400 with the line's unmatched reason.
    /// </summary>
    public static async Task<HttpResponseMessage> FundAsync(
        string accessToken,
        Guid accountId,
        decimal amount,
        bool external = false)
    {
        if (external)
        {
            return await PostAsync(accessToken, Cash(accountId, "TransferIn", amount).WithBookedAt());
        }

        int tenantId;
        string currency;
        string reference;
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == accountId);
            tenantId = account.TenantId;
            currency = account.Currency.Trim();
            reference = account.DepositReference?.Value
                        ?? throw new InvalidOperationException("FundAsync needs a Custodial account or external: true.");
        }

        var clientMoney = await BankFeedHttp.ClientMoneyAccountAsync(tenantId, currency);
        var sent = BankFeedHttp.Feed.AddReceipt(clientMoney.AccountNumber, amount, currency, reference);
        await BankFeedHttp.RunAsync();
        var line = await BankFeedHttp.LineAsync(sent.Id!);
        if (line.Status != BankStatementLineStatus.Matched)
        {
            return Json(HttpStatusCode.BadRequest, new { errors = new { Line = new[] { $"Unmatched: {line.UnmatchedReason}" } } });
        }

        var deposit = (await BankFeedHttp.TransactionsOfLineAsync(line.PublicId)).Single();
        return Json(HttpStatusCode.Created, new { id = deposit.PublicId });
    }

    /// <summary>
    /// Takes cash out of a Custodial account as the platform does (withdrawals §10 fan-out): the Customer's Adviser
    /// requests a withdrawal to a Verified nominated bank account (added straight to the database when the Customer
    /// has none in the currency), a TenantAdmin of the tenant approves it (one is added when there is none), the fake
    /// bank accepts the payment and its debit line is processed. Answers 201 with the payout's id, or the refusal.
    /// </summary>
    public static async Task<HttpResponseMessage> WithdrawAsync(Guid accountId, decimal amount)
    {
        var payee = await PayeeAsync(accountId);
        var adviserToken = (await TestApp.IssueAccessTokenAsync(payee.Adviser)).AccessToken;
        var request = await TransactionHttp.Send(HttpMethod.Post, "/withdrawals", adviserToken, new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["nominatedBankAccountId"] = payee.NominatedBankAccountId,
            ["amount"] = amount
        }, Guid.NewGuid());
        if (request.StatusCode != HttpStatusCode.Created)
        {
            return request;
        }

        var withdrawalId = await ReadIdAsync(request);
        await ApproveAndPayAsync(payee, withdrawalId, amount);

        using var scopeAfter = FunctionalTestSetup.ScopeFactory.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payout = await dbAfter.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.SourceKind == TransactionSourceKind.Payment && row.SourcePublicId == withdrawalId)
            .Select(row => (Guid?)row.PublicId)
            .SingleOrDefaultAsync();
        return payout is null
            ? Json(HttpStatusCode.BadRequest, new { errors = new { Line = new[] { "The payout was not posted." } } })
            : Json(HttpStatusCode.Created, new { id = payout.Value });
    }

    /// <summary>
    /// Trades on a Custodial account as the platform does (orders §10 fan-out): the Customer's Adviser places an
    /// order, the fake broker accepts it and reports one fill of the whole quantity at <paramref name="price"/> (the
    /// instrument's stored price when null), and the report is processed. An order needs a price from the last 4 days
    /// (orders R10), so the instrument's latest price is stored again for today. Answers 201 with the id of the
    /// fill's Transaction, the refusal of the order, or 400 with the reason the fill is held.
    /// </summary>
    public static async Task<HttpResponseMessage> TradeAsync(
        Guid accountId,
        string side,
        Guid instrumentId,
        decimal quantity,
        decimal? price = null,
        Guid? idempotencyKey = null)
    {
        Domain.Entities.User adviser;
        decimal reference;
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == accountId);
            var customer = await db.DomainUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == account.CustomerId);
            adviser = await db.DomainUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == customer.AdviserId);
            var instrument = await db.Instruments.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == instrumentId);
            reference = await db.InstrumentPrices.AsNoTracking()
                .Where(row => row.InstrumentId == instrument.Id)
                .OrderByDescending(row => row.AsOfDate)
                .Select(row => row.Price)
                .FirstOrDefaultAsync();
        }

        if (reference > 0)
        {
            await Orders.OrderHttp.SetPriceAsync(instrumentId, reference, Orders.OrderHttp.Today);
        }

        var adviserToken = (await TestApp.IssueAccessTokenAsync(adviser)).AccessToken;
        var placed = await Orders.OrderHttp.PostAsync(
            adviserToken, Orders.OrderHttp.Body(accountId, instrumentId, side, quantity), idempotencyKey);
        if (placed.StatusCode != HttpStatusCode.Created)
        {
            return placed;
        }

        var orderId = await ReadIdAsync(placed);
        await Broker.BrokerHttp.SendAsync();
        var order = await Orders.OrderHttp.OrderAsync(orderId);
        if (order.Fills.Count == 0)
        {
            Broker.BrokerHttp.Feed.AddFill(orderId, quantity, price ?? reference);
            await Broker.BrokerHttp.RunReportsAsync();
            order = await Orders.OrderHttp.OrderAsync(orderId);
        }

        var fill = order.Fills.SingleOrDefault();
        if (fill?.Status != OrderFillStatus.Posted)
        {
            return Json(HttpStatusCode.BadRequest, new
            {
                errors = new { Fill = new[] { fill is null ? $"Not filled: the order is {order.Status}." : $"Held: {fill.HoldReason}" } }
            });
        }

        using var after = FunctionalTestSetup.ScopeFactory.CreateScope();
        var transaction = await after.ServiceProvider.GetRequiredService<ApplicationDbContext>().Transactions
            .IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == fill.TransactionId)
            .Select(row => row.PublicId)
            .SingleAsync();
        return Json(HttpStatusCode.Created, new { id = transaction });
    }

    /// <summary>
    /// A <c>Buy</c> or <c>Sell</c> with source <c>Manual</c>, written straight to the database at <paramref name="price"/>
    /// as a person could type it on a Custodial account before orders (orders R48), with no fee or, with
    /// <paramref name="withScheduleFee"/>, the fee of the tenant's schedule in effect today. The holding follows. It
    /// can still be reversed by hand. Answers the Transaction's id.
    /// </summary>
    public static async Task<Guid> InsertManualTradeAsync(
        Guid accountId,
        string type,
        Guid instrumentId,
        decimal quantity,
        decimal price = 25m,
        bool withScheduleFee = false)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        var instrument = await db.Instruments.IgnoreQueryFilters().SingleAsync(row => row.PublicId == instrumentId);
        var holding = await db.Holdings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.AccountId == account.Id && row.InstrumentId == instrument.Id);
        var cash = await db.TransactionCashLegs.IgnoreQueryFilters().Where(leg => leg.AccountId == account.Id).SumAsync(leg => leg.Amount);
        var quote = instrument.QuoteCurrency.Trim();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var rate = string.Equals(quote, account.Currency.Trim(), StringComparison.Ordinal)
            ? null
            : AppliedFxRate.Create(quote, account.Currency.Trim(), 1m, today, "Test");
        var schedule = withScheduleFee
            ? await db.TradeFeeSchedules.IgnoreQueryFilters().Include(row => row.BandRows)
                .Where(row => row.TenantId == account.TenantId && row.QuoteCurrency == quote && row.EffectiveFrom <= today)
                .OrderByDescending(row => row.EffectiveFrom)
                .FirstAsync()
            : null;
        var trade = type == "Buy"
            ? Domain.Entities.Transaction.PostBuy(
                account.TenantId, account.Id, account.Type, account.Currency, instrument.Id, quantity, Money.Create(price, quote),
                fxRate: 1m, DateTimeOffset.UtcNow, memo: null, reference: null, TransactionSource.Manual, currentCashSum: cash,
                feeSchedule: schedule, appliedRate: rate)
            : Domain.Entities.Transaction.PostSell(
                account.TenantId, account.Id, account.Type, account.Currency, instrument.Id, quantity, Money.Create(price, quote),
                fxRate: 1m, DateTimeOffset.UtcNow, memo: null, reference: null, TransactionSource.Manual, currentCashSum: cash,
                currentQuantity: holding?.Quantity ?? 0m, currentCost: holding?.CostAmount ?? 0m, feeSchedule: schedule,
                appliedRate: rate);
        trade.CreatedBy = "test";
        trade.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        trade.SecurityLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(trade);
        var moved = trade.SecurityLegs.Single();
        if (holding is null)
        {
            holding = Domain.Entities.Holding.Create(account.TenantId, account.Id, instrument.Id, moved.Quantity, moved.CostAmount, quote);
            holding.CreatedBy = "test";
            db.Holdings.Add(holding);
        }
        else if (holding.ReplaceTotals(holding.Quantity + moved.Quantity, holding.CostAmount + moved.CostAmount, holding.CostCurrency))
        {
            db.Holdings.Remove(holding);
        }

        await db.SaveChangesAsync();
        return trade.PublicId;
    }

    /// <summary>A Verified nominated bank account of the account's Customer in its currency, added straight to the
    /// database when there is none.</summary>
    public static async Task<Guid> VerifiedPayeeAsync(Guid accountId) => (await PayeeAsync(accountId)).NominatedBankAccountId;

    /// <summary>Requests a withdrawal of <paramref name="amount"/> from a Custodial account and leaves it Requested,
    /// so its cash is reserved (withdrawals R36). Answers the withdrawal's id.</summary>
    public static async Task<Guid> RequestWithdrawalAsync(Guid accountId, decimal amount)
    {
        var payee = await PayeeAsync(accountId);
        var adviserToken = (await TestApp.IssueAccessTokenAsync(payee.Adviser)).AccessToken;
        var request = await TransactionHttp.Send(HttpMethod.Post, "/withdrawals", adviserToken, new Dictionary<string, object?>
        {
            ["accountId"] = accountId,
            ["nominatedBankAccountId"] = payee.NominatedBankAccountId,
            ["amount"] = amount
        }, Guid.NewGuid());
        return await ReadIdAsync(request);
    }

    /// <summary>The Customer's Adviser, a TenantAdmin of the tenant to approve (one is added when there is none), and
    /// a Verified nominated bank account in the Account's currency (added straight to the database when there is
    /// none).</summary>
    private static async Task<Payee> PayeeAsync(Guid accountId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == accountId);
        var tenantId = account.TenantId;
        var currency = account.Currency.Trim();
        var customer = await db.DomainUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == account.CustomerId);
        var adviser = await db.DomainUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == customer.AdviserId);
        var approver = await db.DomainUsers.IgnoreQueryFilters().AsNoTracking()
                           .Where(row => row.TenantId == tenantId && row.Role == UserRole.TenantAdmin && row.Status == UserStatus.Active)
                           .OrderBy(row => row.Id)
                           .FirstOrDefaultAsync()
                       ?? await TestApp.CreatePersonAsync(
                           UserRole.TenantAdmin, "Wes Approver", $"wes-{Guid.NewGuid():N}@withdrawals.example", "Password1!", tenantId);
        var nominated = await db.NominatedBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CustomerId == customer.Id && row.Currency == currency && row.Status == NominatedBankAccountStatus.Verified)
            .Select(row => (Guid?)row.PublicId)
            .FirstOrDefaultAsync();
        if (nominated is null)
        {
            var row = Domain.Entities.NominatedBankAccount.Add(
                tenantId, customer.Id, await db.Set<Domain.Entities.Currency>().SingleAsync(item => item.Code == currency),
                "ANZ", customer.Name, "06-0123-0456789-00", adviser.Id, DateTimeOffset.UtcNow);
            row.Verify(approver.Id, "Seeded for a test withdrawal", DateTimeOffset.UtcNow);
            row.CreatedBy = "test";
            db.NominatedBankAccounts.Add(row);
            await db.SaveChangesAsync();
            nominated = row.PublicId;
        }

        return new Payee(tenantId, currency, adviser, approver, nominated.Value);
    }

    /// <summary>The approver approves the withdrawal, the fake bank accepts it and its debit line is processed.</summary>
    private static async Task ApproveAndPayAsync(Payee payee, Guid withdrawalId, decimal amount)
    {
        var approverToken = (await TestApp.IssueAccessTokenAsync(payee.Approver)).AccessToken;
        var item = await AccountHttp.Send(HttpMethod.Get, $"/withdrawals/{withdrawalId}", approverToken);
        using (var json = JsonDocument.Parse(await item.Content.ReadAsStringAsync()))
        {
            var approve = await AccountHttp.Send(HttpMethod.Post, $"/withdrawals/{withdrawalId}/approve", approverToken,
                new { rowVersion = json.RootElement.GetProperty("rowVersion").GetString() });
            approve.StatusCode.ShouldBe(HttpStatusCode.NoContent, await approve.Content.ReadAsStringAsync());
        }

        await BankFeedHttp.SendAsync();
        var clientMoney = await BankFeedHttp.ClientMoneyAccountAsync(payee.TenantId, payee.Currency);
        BankFeedHttp.Feed.Add(clientMoney.AccountNumber, new Application.BankFeed.BankFeedLine(
            "payout-" + Guid.NewGuid().ToString("N"), DateOnly.FromDateTime(DateTime.UtcNow), BankLineDirection.Debit, amount,
            payee.Currency, null, null, null, EndToEndId: withdrawalId.ToString()));
        await BankFeedHttp.RunAsync();
    }

    private sealed record Payee(
        int TenantId,
        string Currency,
        Domain.Entities.User Adviser,
        Domain.Entities.User Approver,
        Guid NominatedBankAccountId);

    /// <summary>A <c>TransferIn</c> with source <c>Manual</c>, written straight to the database, as a person could
    /// enter it on a Custodial account before deposit matching (R35).</summary>
    public static async Task<Guid> InsertManualTransferInAsync(Guid accountId, decimal amount)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(row => row.PublicId == accountId);
        var transaction = Domain.Entities.Transaction.Post(
            account.TenantId, account.Id, TransactionType.TransferIn, amount, account.Currency, DateTimeOffset.UtcNow,
            memo: null, reference: null, TransactionSource.Manual, currentCashSum: 0m, existingTransactionCount: 0);
        transaction.CreatedBy = "test";
        transaction.CashLegs.ForEach(leg => leg.CreatedBy = "test");
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction.PublicId;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
    };

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

    /// <summary>
    /// Closes an account as the platform does. An External account takes <c>POST /accounts/{id}/close</c>. A
    /// Custodial account is closed by an account closure (account-closure §10 fan-out): the Customer's Adviser
    /// requests it, with a Verified nominated bank account when there is cash to pay out; a TenantAdmin approves the
    /// payout's withdrawal and the fake bank's debit line is processed, as <see cref="WithdrawAsync"/> does. Answers
    /// 204 when the account is Closed, or the refusal.
    /// </summary>
    public static async Task<HttpResponseMessage> CloseAsync(string accessToken, Guid accountId)
    {
        bool custodial;
        using (var scope = FunctionalTestSetup.ScopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            custodial = (await db.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.PublicId == accountId))
                .CustodyMode == CustodyMode.Custodial;
        }

        if (!custodial)
        {
            var get = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}", accessToken);
            using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
            var rowVersion = json.RootElement.GetProperty("rowVersion").GetString();
            return await AccountHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/close", accessToken, new { rowVersion });
        }

        var payee = await PayeeAsync(accountId);
        var adviserToken = (await TestApp.IssueAccessTokenAsync(payee.Adviser)).AccessToken;
        var request = await TransactionHttp.Send(HttpMethod.Post, $"/accounts/{accountId}/closures", adviserToken,
            new Dictionary<string, object?>
            {
                ["nominatedBankAccountId"] = payee.NominatedBankAccountId,
                ["memo"] = "Closed for a test"
            }, Guid.NewGuid());
        if (request.StatusCode != HttpStatusCode.Created)
        {
            return request;
        }

        var closureId = await ReadIdAsync(request);
        var closure = await ClosureAsync(adviserToken, accountId, closureId);
        if (closure.GetProperty("status").GetString() == "InProgress")
        {
            var withdrawalId = closure.GetProperty("withdrawalId").GetGuid();
            await ApproveAndPayAsync(payee, withdrawalId, closure.GetProperty("payoutAmount").GetDecimal());
            closure = await ClosureAsync(adviserToken, accountId, closureId);
        }

        return closure.GetProperty("status").GetString() == "Completed"
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : Json(HttpStatusCode.BadRequest, new { errors = new { Closure = new[] { $"The closure is {closure.GetProperty("status").GetString()}." } } });
    }

    private static async Task<JsonElement> ClosureAsync(string accessToken, Guid accountId, Guid closureId)
    {
        var response = await AccountHttp.Send(HttpMethod.Get, $"/accounts/{accountId}/closures/{closureId}", accessToken);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
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
