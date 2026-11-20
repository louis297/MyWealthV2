using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Control;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Bank;

/// <summary>Deposit matching R40, R41, R46 and withdrawals R52–R55: the simulator bank on the control surface.</summary>
public static class BankControlEndpoints
{
    public static RouteGroupBuilder MapBankControlEndpoints(this RouteGroupBuilder control)
    {
        var bank = control.MapGroup("/bank").WithTags("Bank");
        bank.MapGet("/accounts", GetAccountsAsync);
        bank.MapPost("/accounts", OpenAccountAsync);
        bank.MapGet("/accounts/{accountNumber}/statement-lines", GetLinesAsync);
        bank.MapPost("/receipts", AddReceiptAsync);
        bank.MapPost("/payment-mode", SetPaymentModeAsync);
        bank.MapGet("/payments", GetPaymentsAsync);
        bank.MapPost("/payments/{paymentId}/execute", ExecutePaymentAsync);
        bank.MapPost("/payments/{paymentId}/return", ReturnPaymentAsync);
        return control;
    }

    private static async Task<IResult> GetAccountsAsync(SimulatorDbContext db, CancellationToken cancellationToken)
    {
        var accounts = await db.BankAccounts.AsNoTracking().OrderBy(account => account.AccountNumber).ToListAsync(cancellationToken);
        var balances = await BalancesAsync(db, cancellationToken);
        var items = accounts.Select(account => BankAccountItem.From(account, balances.GetValueOrDefault(account.Id))).ToList();
        return Results.Ok(new BankAccountList(items));
    }

    private static async Task<IResult> OpenAccountAsync(
        OpenBankAccountRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        if (!BankRules.IsAccountNumber(body.AccountNumber))
        {
            return ControlEndpoints.Refused("AccountNumber",
                "accountNumber must be 1 to 34 letters, digits, spaces and hyphens, with no two spaces in a row.");
        }

        if (!BankRules.IsCurrency(body.Currency))
        {
            return ControlEndpoints.Refused("Currency", "currency must be three upper-case letters.");
        }

        if (!BankRules.IsName(body.Name))
        {
            return ControlEndpoints.Refused("Name", "name must be 1 to 140 characters.");
        }

        var number = body.AccountNumber!.Trim();
        if (await db.BankAccounts.AnyAsync(account => account.AccountNumber == number, http.RequestAborted))
        {
            return ControlEndpoints.Refused("AccountNumber", "An account with this number is already open.");
        }

        var world = await db.Worlds.AsNoTracking().SingleAsync(http.RequestAborted);
        var realNow = time.GetUtcNow();
        var account = BankAccount.Open(number, body.Currency!, body.Name!.Trim(), world.Now(realNow));
        db.BankAccounts.Add(account);
        Log(db, http, world, realNow, ControlActions.BankAccountOpened, body);
        await db.SaveChangesAsync(http.RequestAborted);
        return Results.Created($"/control/bank/accounts/{Uri.EscapeDataString(account.AccountNumber)}", BankAccountItem.From(account, 0m));
    }

    private static async Task<IResult> AddReceiptAsync(
        AddReceiptRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var number = body.AccountNumber?.Trim() ?? "";
        var account = number.Length == 0
            ? null
            : await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(row => row.AccountNumber == number, http.RequestAborted);
        if (account is null)
        {
            return ControlEndpoints.Refused("AccountNumber", "accountNumber must be an open account.");
        }

        if (!BankRules.IsAmount(body.Amount))
        {
            return ControlEndpoints.Refused("Amount", "amount must be greater than zero, with at most 4 decimal places.");
        }

        if (body.Reference?.Length > BankRules.FreeTextMaxLength)
        {
            return ControlEndpoints.Refused("Reference", "reference must be at most 140 characters.");
        }

        if (body.PayerName?.Length > BankRules.FreeTextMaxLength)
        {
            return ControlEndpoints.Refused("PayerName", "payerName must be at most 140 characters.");
        }

        if (body.PayerAccount?.Length > BankRules.AccountNumberMaxLength)
        {
            return ControlEndpoints.Refused("PayerAccount", "payerAccount must be at most 34 characters.");
        }

        var availableAfter = BankRules.AvailableAfter(body.AvailableAfter);
        if (availableAfter is null)
        {
            return ControlEndpoints.Refused("AvailableAfter", "availableAfter must be an ISO 8601 duration from PT0S to P30D.");
        }

        var world = await db.Worlds.SingleAsync(http.RequestAborted);
        var realNow = time.GetUtcNow();
        var simulatorNow = world.Now(realNow);
        var today = DateOnly.FromDateTime(simulatorNow.UtcDateTime);
        var valueDate = body.ValueDate ?? today;
        if (valueDate > today)
        {
            return ControlEndpoints.Refused("ValueDate", "valueDate cannot be after the simulator date.");
        }

        var line = BankLine.Receipt(
            world.TakeLineOutsideId(),
            account.Id,
            body.Amount!.Value,
            valueDate,
            BankRules.Text(body.PayerName),
            BankRules.Text(body.PayerAccount),
            BankRules.Text(body.Reference),
            simulatorNow,
            simulatorNow + availableAfter.Value,
            body.DeliverTwice ?? false);
        db.BankLines.Add(line);
        Log(db, http, world, realNow, ControlActions.BankReceiptAdded, body);
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The world was changed by another write. Read it again and retry.");
        }

        return Results.Created($"/control/bank/accounts/{Uri.EscapeDataString(account.AccountNumber)}/statement-lines",
            new AddedReceipt(line.OutsideId));
    }

    /// <summary>Withdrawals R52: how the bank answers new payments. A reset sets <c>Execute</c> (R56).</summary>
    private static Task<IResult> SetPaymentModeAsync(
        PaymentModeRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        if (!Enum.TryParse<BankPaymentMode>(body.Mode, ignoreCase: false, out var mode)
            || !Enum.IsDefined(mode)
            || int.TryParse(body.Mode, out _))
        {
            return Task.FromResult(ControlEndpoints.Refused("Mode", "mode must be Execute, Hold or Reject."));
        }

        return ControlEndpoints.WriteAsync(http, db, time, ControlActions.BankPaymentModeChanged, body,
            (world, _) => world.SetPaymentMode(mode, body.Reason));
    }

    /// <summary>Withdrawals R53: a held payment is executed, and its debit line enters the feed at once.</summary>
    private static async Task<IResult> ExecutePaymentAsync(
        string paymentId,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var payment = await db.BankPayments.SingleOrDefaultAsync(row => row.PaymentId == paymentId, http.RequestAborted);
        if (payment is null)
        {
            return Results.NotFound();
        }

        if (payment.Status != BankPaymentStatus.Held)
        {
            return ControlEndpoints.Refused("Status", $"Only a held payment can be executed; this one is {payment.Status}.");
        }

        var world = await db.Worlds.SingleAsync(http.RequestAborted);
        var realNow = time.GetUtcNow();
        var simulatorNow = world.Now(realNow);
        var line = BankLine.Payment(world.TakeLineOutsideId(), payment, DateOnly.FromDateTime(simulatorNow.UtcDateTime), simulatorNow);
        return await SaveWithLineAsync(db, http, world, realNow, line, ControlActions.BankPaymentExecuted, new { paymentId },
            payment, () => payment.Executed(line.Id));
    }

    /// <summary>Withdrawals R54: the payee's bank sends an executed payment back, as a credit line that names its debit
    /// line, available after <c>availableAfter</c>.</summary>
    private static async Task<IResult> ReturnPaymentAsync(
        string paymentId,
        ReturnPaymentRequest body,
        HttpContext http,
        SimulatorDbContext db,
        TimeProvider time)
    {
        var payment = await db.BankPayments.SingleOrDefaultAsync(row => row.PaymentId == paymentId, http.RequestAborted);
        if (payment is null)
        {
            return Results.NotFound();
        }

        if (payment.Status != BankPaymentStatus.Executed || payment.DebitLineId is not long debitLineId)
        {
            return ControlEndpoints.Refused("Status", $"Only an executed payment can be returned; this one is {payment.Status}.");
        }

        var reason = body.Reason?.Trim();
        if (reason is not { Length: >= 1 and <= BankRules.FreeTextMaxLength })
        {
            return ControlEndpoints.Refused("Reason", "reason must be 1 to 140 characters.");
        }

        var availableAfter = BankRules.AvailableAfter(body.AvailableAfter);
        if (availableAfter is null)
        {
            return ControlEndpoints.Refused("AvailableAfter", "availableAfter must be an ISO 8601 duration from PT0S to P30D.");
        }

        var world = await db.Worlds.SingleAsync(http.RequestAborted);
        var realNow = time.GetUtcNow();
        var simulatorNow = world.Now(realNow);
        var line = BankLine.PaymentReturn(
            world.TakeLineOutsideId(), payment, debitLineId, reason, DateOnly.FromDateTime(simulatorNow.UtcDateTime),
            simulatorNow, simulatorNow + availableAfter.Value);
        return await SaveWithLineAsync(db, http, world, realNow, line, ControlActions.BankPaymentReturned,
            new { paymentId, reason, body.AvailableAfter }, payment, () => payment.Returned(line.Id));
    }

    /// <summary>Withdrawals R55: the payments, newest first, with the lines they caused.</summary>
    private static async Task<IResult> GetPaymentsAsync(
        SimulatorDbContext db,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var number = Math.Max(page ?? 1, 1);
        var total = await db.BankPayments.CountAsync(cancellationToken);
        var rows = await (
                from payment in db.BankPayments.AsNoTracking()
                join account in db.BankAccounts.AsNoTracking() on payment.BankAccountId equals account.Id
                orderby payment.Id descending
                select new
                {
                    payment,
                    account.AccountNumber,
                    account.Currency,
                    Debit = db.BankLines.Where(line => line.Id == payment.DebitLineId).Select(line => line.OutsideId).FirstOrDefault(),
                    Return = db.BankLines.Where(line => line.Id == payment.ReturnLineId).Select(line => line.OutsideId).FirstOrDefault()
                })
            .Skip((number - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
        var items = rows.Select(row => PaymentItem.From(row.payment, row.Debit, row.Return) with
        {
            AccountNumber = row.AccountNumber,
            Currency = row.Currency.Trim()
        }).ToList();
        return Results.Ok(new PaymentPage(items, number, size, total));
    }

    /// <summary>Adds <paramref name="line"/>, links it with <paramref name="link"/> and logs the action, in one
    /// transaction. A concurrent write on the world row is 409.</summary>
    private static async Task<IResult> SaveWithLineAsync(
        SimulatorDbContext db,
        HttpContext http,
        World world,
        DateTimeOffset realNow,
        BankLine line,
        string action,
        object detail,
        BankPayment payment,
        Action link)
    {
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted);
                db.BankLines.Add(line);
                Log(db, http, world, realNow, action, detail);
                await db.SaveChangesAsync(http.RequestAborted);
                link();
                await db.SaveChangesAsync(http.RequestAborted);
                await transaction.CommitAsync(http.RequestAborted);
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The world was changed by another write. Read it again and retry.");
        }

        var debit = await db.BankLines.AsNoTracking().Where(row => row.Id == payment.DebitLineId).Select(row => row.OutsideId)
            .FirstOrDefaultAsync(http.RequestAborted);
        var returned = await db.BankLines.AsNoTracking().Where(row => row.Id == payment.ReturnLineId).Select(row => row.OutsideId)
            .FirstOrDefaultAsync(http.RequestAborted);
        return Results.Ok(PaymentItem.From(payment, debit, returned));
    }

    private static async Task<IResult> GetLinesAsync(string accountNumber, SimulatorDbContext db, CancellationToken cancellationToken)
    {
        var account = await db.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountNumber == accountNumber, cancellationToken);
        if (account is null)
        {
            return Results.NotFound();
        }

        var lines = await db.BankLines.AsNoTracking()
            .Where(line => line.BankAccountId == account.Id)
            .OrderBy(line => line.Id)
            .ToListAsync(cancellationToken);
        var places = (await db.BankFeedPlaces.AsNoTracking()
                .Where(place => place.BankAccountId == account.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(place => place.BankLineId, place => place.Place);
        var items = lines.Select(line => new BankLineItem(
            line.OutsideId,
            line.ValueDate,
            line.Direction == BankLine.CreditDirection ? "Credit" : "Debit",
            line.Amount,
            account.Currency,
            line.PayerName,
            line.PayerAccount,
            line.Reference,
            line.PostedAt,
            line.AvailableAt,
            line.DeliverTwice,
            places[line.Id].Order().ToList())).ToList();
        return Results.Ok(new BankLineList(items));
    }

    private static async Task<Dictionary<int, decimal>> BalancesAsync(SimulatorDbContext db, CancellationToken cancellationToken) =>
        await db.BankLines.AsNoTracking()
            .GroupBy(line => line.BankAccountId)
            .Select(group => new { group.Key, Balance = group.Sum(line => line.Direction == BankLine.CreditDirection ? line.Amount : -line.Amount) })
            .ToDictionaryAsync(row => row.Key, row => row.Balance, cancellationToken);

    private static void Log(SimulatorDbContext db, HttpContext http, World world, DateTimeOffset realNow, string action, object body) =>
        db.ControlLog.Add(ControlLogEntry.Create(
            realNow,
            world.Now(realNow),
            ControlEndpoints.Actor(http.User),
            action,
            JsonSerializer.Serialize(body, body.GetType(), ControlEndpoints.DetailJson)));
}

public sealed record OpenBankAccountRequest(string? AccountNumber, string? Currency, string? Name);

public sealed record AddReceiptRequest(
    string? AccountNumber,
    decimal? Amount,
    string? Reference,
    string? PayerName,
    string? PayerAccount,
    DateOnly? ValueDate,
    string? AvailableAfter,
    bool? DeliverTwice);

public sealed record AddedReceipt(string Id);

public sealed record BankAccountItem(string AccountNumber, string Currency, string Name, DateTimeOffset OpenedAt, decimal Balance)
{
    public static BankAccountItem From(BankAccount account, decimal balance) =>
        new(account.AccountNumber, account.Currency.Trim(), account.Name, account.OpenedAt, balance);
}

public sealed record BankAccountList(IReadOnlyList<BankAccountItem> Items);

public sealed record BankLineItem(
    string Id,
    DateOnly ValueDate,
    string Direction,
    decimal Amount,
    string Currency,
    string? PayerName,
    string? PayerAccount,
    string? Reference,
    DateTimeOffset PostedAt,
    DateTimeOffset AvailableAt,
    bool DeliverTwice,
    IReadOnlyList<long> Places);

public sealed record BankLineList(IReadOnlyList<BankLineItem> Items);

public sealed record PaymentModeRequest(string? Mode, string? Reason);

public sealed record ReturnPaymentRequest(string? Reason, string? AvailableAfter);

/// <summary>Withdrawals R55: a payment in the simulator bank's books.</summary>
public sealed record PaymentItem(
    string PaymentId,
    string AccountNumber,
    string Currency,
    string InstructionId,
    decimal Amount,
    string? Reference,
    string PayeeAccountNumber,
    string PayeeName,
    string Status,
    string Mode,
    string? RejectReason,
    DateTimeOffset ReceivedAt,
    string? DebitLineId,
    string? ReturnLineId)
{
    public static PaymentItem From(BankPayment payment, string? debitLineId, string? returnLineId) => new(
        payment.PaymentId,
        "",
        "",
        payment.InstructionId,
        payment.Amount,
        payment.Reference,
        payment.PayeeAccountNumber,
        payment.PayeeName,
        payment.Status.ToString(),
        payment.Mode.ToString(),
        payment.RejectReason,
        payment.ReceivedAt,
        debitLineId,
        returnLineId);
}

public sealed record PaymentPage(IReadOnlyList<PaymentItem> Items, int Page, int PageSize, int TotalCount);
