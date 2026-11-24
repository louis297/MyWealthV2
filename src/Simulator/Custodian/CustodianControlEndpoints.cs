using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Broker;
using MyWealthV2.Simulator.Control;
using MyWealthV2.Simulator.Data;
using MyWealthV2.Simulator.Market;
using MyWealthV2.Simulator.Worlds;

namespace MyWealthV2.Simulator.Custodian;

/// <summary>In-kind-transfers R61, R64–R66, R68, R70: the simulator custodian on the control surface.</summary>
public static class CustodianControlEndpoints
{
    public static RouteGroupBuilder MapCustodianControlEndpoints(this RouteGroupBuilder control)
    {
        var custodian = control.MapGroup("/custodian").WithTags("Custodian");
        custodian.MapGet("", GetSettingsAsync);
        custodian.MapGet("/accounts", GetAccountsAsync);
        custodian.MapPost("/accounts", OpenAccountAsync);
        custodian.MapGet("/accounts/{accountNumber}/movements", GetMovementsAsync);
        custodian.MapGet("/transfers", GetTransfersAsync);
        custodian.MapPost("/transfer-mode", SetTransferModeAsync);
        custodian.MapPost("/transfers/{transferId}/execute", ExecuteAsync);
        custodian.MapPost("/movements", AddMovementAsync);
        custodian.MapPost("/rounding-mode", SetRoundingModeAsync);
        custodian.MapPost("/accounts/{accountNumber}/income-account", SetIncomeAccountAsync);
        custodian.MapPost("/income-mode", SetIncomeModeAsync);
        custodian.MapGet("/dividend-payments", GetDividendPaymentsAsync);
        custodian.MapPost("/dividend-payments/{id}/pay", PayDividendAsync);
        return control;
    }

    private static async Task<IResult> GetSettingsAsync(SimulatorDbContext db, TimeProvider time, BrokerBankAccounts brokerAccounts,
        CancellationToken cancellationToken)
    {
        await BrokerBook.UpToDateAsync(db, time, brokerAccounts, cancellationToken);
        var settings = await db.CustodianSettings.AsNoTracking().SingleAsync(cancellationToken);
        return Results.Ok(Settings(settings));
    }

    private static CustodianSettingsItem Settings(CustodianSettings settings) => new(
        settings.TransferMode.ToString(), settings.RejectReason, settings.RoundingMode.ToString(), settings.IncomeMode.ToString());

    private static async Task<IResult> GetAccountsAsync(SimulatorDbContext db, TimeProvider time, BrokerBankAccounts brokerAccounts,
        CancellationToken cancellationToken)
    {
        await BrokerBook.UpToDateAsync(db, time, brokerAccounts, cancellationToken);
        var accounts = await db.CustodianAccounts.AsNoTracking().OrderBy(row => row.AccountNumber).ToListAsync(cancellationToken);
        var items = new List<CustodianAccountItem>();
        foreach (var account in accounts)
        {
            var positions = await CustodianBook.PositionsAsync(db, account.AccountNumber, null, cancellationToken);
            items.Add(new CustodianAccountItem(account.AccountNumber, account.Currency.Trim(), account.Name,
                positions.Select(position => new CustodianPositionItem(position.Symbol, account.Currency.Trim(), position.Quantity)).ToList(),
                account.IncomeAccountNumber));
        }

        return Results.Ok(new CustodianAccountList(items));
    }

    private static async Task<IResult> OpenAccountAsync(OpenCustodianAccountRequest body, HttpContext http, SimulatorDbContext db, TimeProvider time)
    {
        if (!CustodianRules.IsAccountNumber(body.AccountNumber))
        {
            return ControlEndpoints.Refused("AccountNumber",
                "accountNumber must be 1 to 34 letters, digits, spaces and hyphens, with no two spaces in a row.");
        }

        if (!CustodianRules.IsCurrency(body.Currency))
        {
            return ControlEndpoints.Refused("Currency", "currency must be three upper-case letters.");
        }

        if (!CustodianRules.IsName(body.Name))
        {
            return ControlEndpoints.Refused("Name", "name must be 1 to 140 characters.");
        }

        if (body.IncomeAccountNumber is not null && !CustodianRules.IsAccountNumber(body.IncomeAccountNumber))
        {
            return ControlEndpoints.Refused("IncomeAccountNumber", IncomeAccountMessage);
        }

        var number = body.AccountNumber!.Trim();
        if (await db.CustodianAccounts.AnyAsync(row => row.AccountNumber == number, http.RequestAborted))
        {
            return ControlEndpoints.Refused("AccountNumber", "An account with this number is already open.");
        }

        var world = await db.Worlds.AsNoTracking().SingleAsync(http.RequestAborted);
        var realNow = time.GetUtcNow();
        var account = CustodianAccount.Open(number, body.Currency!, body.Name!.Trim(), world.Now(realNow), body.IncomeAccountNumber?.Trim());
        db.CustodianAccounts.Add(account);
        Log(db, http, world, realNow, ControlActions.CustodianAccountOpened, body);
        await db.SaveChangesAsync(http.RequestAborted);
        return Results.Created($"/control/custodian/accounts/{Uri.EscapeDataString(account.AccountNumber)}",
            new CustodianAccountItem(account.AccountNumber, account.Currency.Trim(), account.Name, [], account.IncomeAccountNumber));
    }

    private static async Task<IResult> GetMovementsAsync(
        string accountNumber, int? page, int? pageSize, SimulatorDbContext db, TimeProvider time, BrokerBankAccounts brokerAccounts,
        CancellationToken cancellationToken)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, brokerAccounts, cancellationToken);
        var account = await db.CustodianAccounts.AsNoTracking().SingleOrDefaultAsync(row => row.AccountNumber == accountNumber, cancellationToken);
        if (account is null)
        {
            return Results.NotFound();
        }

        var size = Math.Clamp(pageSize ?? ControlEndpoints.DefaultPageSize, 1, ControlEndpoints.MaxPageSize);
        var number = Math.Max(page ?? 1, 1);
        var query = db.CustodianMovements.AsNoTracking().Where(row => row.AccountNumber == account.AccountNumber);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(row => row.Place).Skip((number - 1) * size).Take(size).ToListAsync(cancellationToken);
        var items = rows.Select(row => new CustodianControlMovementItem(
            CustodianMovement.MovementId(world.WorldId, row.Number), row.Direction == CustodianMovement.InDirection ? "In" : "Out",
            row.Kind.ToString(), row.Symbol, row.Currency.Trim(), row.Quantity,
            row.SettlementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), row.InstructionId, row.TradeReference,
            row.CorporateActionId, row.CounterpartyName, row.CounterpartyAccount, row.Reference, row.OmitFromFeed)).ToList();
        return Results.Ok(new CustodianMovementPage(items, number, size, total));
    }

    private static async Task<IResult> GetTransfersAsync(
        int? page, int? pageSize, SimulatorDbContext db, TimeProvider time, BrokerBankAccounts brokerAccounts, CancellationToken cancellationToken)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, brokerAccounts, cancellationToken);
        var size = Math.Clamp(pageSize ?? ControlEndpoints.DefaultPageSize, 1, ControlEndpoints.MaxPageSize);
        var number = Math.Max(page ?? 1, 1);
        var total = await db.CustodianTransfers.CountAsync(cancellationToken);
        var rows = await db.CustodianTransfers.AsNoTracking().OrderByDescending(row => row.Number)
            .Skip((number - 1) * size).Take(size).ToListAsync(cancellationToken);
        var items = rows.Select(row => new CustodianTransferItem(
            CustodianTransfer.TransferId(world.WorldId, row.Number), row.AccountNumber, row.InstructionId, row.Status.ToString(),
            row.ModeAtReceipt.ToString(), row.RejectReason,
            row.MovementNumber is int movement ? CustodianMovement.MovementId(world.WorldId, movement) : null, row.ReceivedAt)).ToList();
        return Results.Ok(new CustodianTransferPage(items, number, size, total));
    }

    /// <summary>R64: the transfer mode, with a reason for <c>Reject</c>.</summary>
    private static async Task<IResult> SetTransferModeAsync(TransferModeRequest body, HttpContext http, SimulatorDbContext db, TimeProvider time)
    {
        if (!Enum.TryParse<CustodianTransferMode>(body.Mode, ignoreCase: false, out var mode) || !Enum.IsDefined(mode))
        {
            return ControlEndpoints.Refused("Mode", "mode must be Execute, Hold or Reject.");
        }

        if (mode == CustodianTransferMode.Reject && !CustodianRules.RejectReasons.Contains(body.Reason))
        {
            return ControlEndpoints.Refused("Reason", $"reason must be one of {string.Join(", ", CustodianRules.RejectReasons)}.");
        }

        var settings = await db.CustodianSettings.SingleAsync(http.RequestAborted);
        settings.SetTransferMode(mode, body.Reason);
        var world = await db.Worlds.AsNoTracking().SingleAsync(http.RequestAborted);
        Log(db, http, world, time.GetUtcNow(), ControlActions.CustodianTransferModeChanged, body);
        await db.SaveChangesAsync(http.RequestAborted);
        return Results.Ok(Settings(settings));
    }

    /// <summary>R65: executes a held transfer, with the instructed quantity or a typed one.</summary>
    private static async Task<IResult> ExecuteAsync(
        string transferId, ExecuteTransferRequest body, HttpContext http, SimulatorDbContext db, TimeProvider time,
        BrokerBankAccounts brokerAccounts)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, brokerAccounts, http.RequestAborted);
        if (!CustodianTransfer.TryReadNumber(transferId, world.WorldId, out var number))
        {
            return Results.NotFound();
        }

        var transfer = await db.CustodianTransfers.SingleOrDefaultAsync(row => row.Number == number, http.RequestAborted);
        if (transfer is null)
        {
            return Results.NotFound();
        }

        if (transfer.Status != CustodianTransferStatus.Held)
        {
            return ControlEndpoints.Refused("Status", $"The transfer is {transfer.Status}; only a held transfer can be executed.");
        }

        if (body.Quantity is not null && !CustodianRules.IsQuantity(body.Quantity))
        {
            return ControlEndpoints.Refused("Quantity", "quantity must be above 0 with at most 8 decimal places.");
        }

        var request = JsonSerializer.Deserialize<TransferRequest>(transfer.Body)!;
        var realNow = time.GetUtcNow();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted);
            await CustodianBook.LockAsync(db, http.RequestAborted);
            await CustodianPartyEndpoints.ExecuteAsync(db, transfer, request, body.Quantity ?? request.Quantity!.Value, world.Now(realNow),
                http.RequestAborted);
            Log(db, http, world, realNow, ControlActions.CustodianTransferExecuted, new { transferId, body.Quantity });
            await db.SaveChangesAsync(http.RequestAborted);
            await transaction.CommitAsync(http.RequestAborted);
        });
        return Results.Ok(new { transferId, movementId = CustodianMovement.MovementId(world.WorldId, transfer.MovementNumber!.Value) });
    }

    /// <summary>R66: a movement nobody asked for, of kind <c>Transfer</c>, optionally left out of the feed.</summary>
    private static async Task<IResult> AddMovementAsync(
        AddCustodianMovementRequest body, HttpContext http, SimulatorDbContext db, TimeProvider time, BrokerBankAccounts brokerAccounts)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, brokerAccounts, http.RequestAborted);
        var account = body.AccountNumber is null
            ? null
            : await db.CustodianAccounts.AsNoTracking().SingleOrDefaultAsync(row => row.AccountNumber == body.AccountNumber, http.RequestAborted);
        if (account is null)
        {
            return ControlEndpoints.Refused("AccountNumber", "accountNumber must be an open account of the custodian.");
        }

        if (!CustodianRules.IsDirection(body.Direction))
        {
            return ControlEndpoints.Refused("Direction", "direction must be In or Out.");
        }

        if (!CustodianRules.IsSymbol(body.Symbol))
        {
            return ControlEndpoints.Refused("Symbol", "symbol must be 1 to 32 characters.");
        }

        if (!CustodianRules.IsQuantity(body.Quantity))
        {
            return ControlEndpoints.Refused("Quantity", "quantity must be above 0 with at most 8 decimal places.");
        }

        if ((body.CounterpartyName?.Length ?? 0) > CustodianRules.FreeTextMaxLength
            || (body.CounterpartyAccount?.Length ?? 0) > CustodianRules.CounterpartyAccountMaxLength
            || (body.Reference?.Length ?? 0) > CustodianRules.FreeTextMaxLength)
        {
            return ControlEndpoints.Refused("Reference", "counterpartyName and reference are at most 140 characters, counterpartyAccount at most 34.");
        }

        var realNow = time.GetUtcNow();
        CustodianMovement? movement = null;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted);
            await CustodianBook.LockAsync(db, http.RequestAborted);
            var simulatorNow = world.Now(realNow);
            movement = await CustodianBook.AddAsync(db, account.AccountNumber, CustodianRules.Direction(body.Direction!),
                CustodianMovementKind.Transfer, body.Symbol!.Trim(), account.Currency.Trim(), body.Quantity!.Value,
                MarketPricePath.Day(simulatorNow), simulatorNow,
                counterpartyName: CustodianRules.Text(body.CounterpartyName, CustodianRules.FreeTextMaxLength),
                counterpartyAccount: CustodianRules.Text(body.CounterpartyAccount, CustodianRules.CounterpartyAccountMaxLength),
                reference: CustodianRules.Text(body.Reference, CustodianRules.FreeTextMaxLength),
                omitFromFeed: body.OmitFromFeed ?? false, cancellationToken: http.RequestAborted);
            Log(db, http, world, realNow, ControlActions.CustodianMovementAdded, body);
            await db.SaveChangesAsync(http.RequestAborted);
            await transaction.CommitAsync(http.RequestAborted);
        });
        return Results.Created($"/control/custodian/accounts/{Uri.EscapeDataString(account.AccountNumber)}/movements",
            new { id = CustodianMovement.MovementId(world.WorldId, movement!.Number) });
    }

    /// <summary>R68: <c>Fractional</c> or <c>WholeShares</c>.</summary>
    private static async Task<IResult> SetRoundingModeAsync(RoundingModeRequest body, HttpContext http, SimulatorDbContext db, TimeProvider time)
    {
        if (!Enum.TryParse<CustodianRoundingMode>(body.Mode, ignoreCase: false, out var mode) || !Enum.IsDefined(mode))
        {
            return ControlEndpoints.Refused("Mode", "mode must be Fractional or WholeShares.");
        }

        var settings = await db.CustodianSettings.SingleAsync(http.RequestAborted);
        settings.SetRoundingMode(mode);
        var world = await db.Worlds.AsNoTracking().SingleAsync(http.RequestAborted);
        Log(db, http, world, time.GetUtcNow(), ControlActions.CustodianRoundingModeChanged, body);
        await db.SaveChangesAsync(http.RequestAborted);
        return Results.Ok(Settings(settings));
    }

    private const string IncomeAccountMessage =
        "incomeAccountNumber must be 1 to 34 letters, digits, spaces and hyphens, with no two spaces in a row.";

    /// <summary>Dividends R57: sets or clears the bank account an account's income is paid into.</summary>
    private static async Task<IResult> SetIncomeAccountAsync(
        string accountNumber, IncomeAccountRequest body, HttpContext http, SimulatorDbContext db, TimeProvider time)
    {
        var account = await db.CustodianAccounts.SingleOrDefaultAsync(row => row.AccountNumber == accountNumber, http.RequestAborted);
        if (account is null)
        {
            return Results.NotFound();
        }

        if (body.IncomeAccountNumber is not null && !CustodianRules.IsAccountNumber(body.IncomeAccountNumber))
        {
            return ControlEndpoints.Refused("IncomeAccountNumber", IncomeAccountMessage);
        }

        account.SetIncomeAccount(body.IncomeAccountNumber?.Trim());
        var world = await db.Worlds.AsNoTracking().SingleAsync(http.RequestAborted);
        Log(db, http, world, time.GetUtcNow(), ControlActions.CustodianIncomeAccountChanged, new { accountNumber, body.IncomeAccountNumber });
        await db.SaveChangesAsync(http.RequestAborted);
        var positions = await CustodianBook.PositionsAsync(db, account.AccountNumber, null, http.RequestAborted);
        return Results.Ok(new CustodianAccountItem(account.AccountNumber, account.Currency.Trim(), account.Name,
            positions.Select(position => new CustodianPositionItem(position.Symbol, account.Currency.Trim(), position.Quantity)).ToList(),
            account.IncomeAccountNumber));
    }

    /// <summary>Dividends R60: <c>Pay</c> or <c>Hold</c>.</summary>
    private static async Task<IResult> SetIncomeModeAsync(IncomeModeRequest body, HttpContext http, SimulatorDbContext db, TimeProvider time)
    {
        if (!Enum.TryParse<CustodianIncomeMode>(body.Mode, ignoreCase: false, out var mode) || !Enum.IsDefined(mode)
            || body.Mode!.All(char.IsAsciiDigit))
        {
            return ControlEndpoints.Refused("Mode", "mode must be Pay or Hold.");
        }

        var settings = await db.CustodianSettings.SingleAsync(http.RequestAborted);
        settings.SetIncomeMode(mode);
        var world = await db.Worlds.AsNoTracking().SingleAsync(http.RequestAborted);
        Log(db, http, world, time.GetUtcNow(), ControlActions.CustodianIncomeModeChanged, body);
        await db.SaveChangesAsync(http.RequestAborted);
        return Results.Ok(Settings(settings));
    }

    /// <summary>Dividends R62: the dividend payments, newest first, with an optional status and account.</summary>
    private static async Task<IResult> GetDividendPaymentsAsync(
        string? status, string? accountNumber, int? page, int? pageSize, SimulatorDbContext db, TimeProvider time,
        BrokerBankAccounts brokerAccounts, CancellationToken cancellationToken)
    {
        var world = await BrokerBook.UpToDateAsync(db, time, brokerAccounts, cancellationToken);
        CustodianDividendPaymentStatus? wanted = null;
        if (status is not null)
        {
            if (!Enum.TryParse<CustodianDividendPaymentStatus>(status, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed)
                || status.All(char.IsAsciiDigit))
            {
                return ControlEndpoints.Refused("Status", "status must be Due, Held or Paid.");
            }

            wanted = parsed;
        }

        var size = Math.Clamp(pageSize ?? ControlEndpoints.DefaultPageSize, 1, ControlEndpoints.MaxPageSize);
        var number = Math.Max(page ?? 1, 1);
        var query =
            from payment in db.CustodianDividendPayments.AsNoTracking()
            join account in db.CustodianAccounts.AsNoTracking() on payment.AccountId equals account.Id
            join action in db.MarketCorporateActions.AsNoTracking() on payment.ActionId equals action.Id
            join line in db.BankLines.AsNoTracking() on payment.BankLineId equals (long?)line.Id into lines
            from line in lines.DefaultIfEmpty()
            join movement in db.CustodianMovements.AsNoTracking() on payment.MovementId equals (int?)movement.Id into movements
            from movement in movements.DefaultIfEmpty()
            where (wanted == null || payment.Status == wanted) && (accountNumber == null || account.AccountNumber == accountNumber)
            select new
            {
                payment,
                account.AccountNumber,
                account.IncomeAccountNumber,
                action.Number,
                LineId = line == null ? null : line.OutsideId,
                MovementNumber = movement == null ? (int?)null : movement.Number
            };
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(row => row.payment.Id).Skip((number - 1) * size).Take(size).ToListAsync(cancellationToken);
        var items = rows.Select(row => new CustodianDividendPaymentItem(
            row.payment.Id.ToString(CultureInfo.InvariantCulture),
            MarketCorporateAction.ActionId(world.WorldId, row.Number),
            row.AccountNumber,
            row.payment.EntitledPosition,
            row.payment.ElectedQuantity,
            row.payment.Shares,
            row.payment.Cash,
            row.payment.BankLineId is null ? null : row.IncomeAccountNumber,
            row.payment.Status.ToString(),
            row.LineId,
            row.MovementNumber is int movementNumber ? CustodianMovement.MovementId(world.WorldId, movementNumber) : null)).ToList();
        return Results.Ok(new CustodianDividendPaymentPage(items, number, size, total));
    }

    /// <summary>Dividends R60: pays a <c>Held</c> payment, with another cash amount when given.</summary>
    private static async Task<IResult> PayDividendAsync(
        string id, PayDividendRequest body, HttpContext http, SimulatorDbContext db, TimeProvider time, BrokerBankAccounts brokerAccounts)
    {
        await BrokerBook.UpToDateAsync(db, time, brokerAccounts, http.RequestAborted);
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var paymentId))
        {
            return Results.NotFound();
        }

        if (body.Amount is { } amount && (amount <= 0m || decimal.Round(amount, 2) != amount))
        {
            return ControlEndpoints.Refused("Amount", "amount must be above 0, with at most 2 decimal places.");
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted);
            await CustodianBook.LockAsync(db, http.RequestAborted);
            var payment = await db.CustodianDividendPayments.SingleOrDefaultAsync(row => row.Id == paymentId, http.RequestAborted);
            if (payment is null)
            {
                return Results.NotFound();
            }

            if (payment.Status != CustodianDividendPaymentStatus.Held)
            {
                return ControlEndpoints.Refused("Status", $"The dividend payment is {payment.Status} and cannot be paid by hand.");
            }

            var dividend = await db.MarketCorporateActions.SingleAsync(row => row.Id == payment.ActionId, http.RequestAborted);
            await CustodianDividends.PayAsync(db, time, dividend, payment, body.Amount, http.RequestAborted);
            var world = await db.Worlds.AsNoTracking().SingleAsync(http.RequestAborted);
            Log(db, http, world, time.GetUtcNow(), ControlActions.CustodianDividendPaid, new { id, body.Amount });
            await db.SaveChangesAsync(http.RequestAborted);
            await transaction.CommitAsync(http.RequestAborted);
            return Results.Ok(new { id, status = payment.Status.ToString(), cash = payment.Cash });
        });
    }

    private static void Log(SimulatorDbContext db, HttpContext http, World world, DateTimeOffset realNow, string action, object body) =>
        db.ControlLog.Add(ControlLogEntry.Create(
            realNow, world.Now(realNow), ControlEndpoints.Actor(http.User), action,
            JsonSerializer.Serialize(body, body.GetType(), ControlEndpoints.DetailJson)));
}

public sealed record CustodianSettingsItem(string TransferMode, string? RejectReason, string RoundingMode, string IncomeMode);

public sealed record CustodianAccountItem(
    string AccountNumber, string Currency, string Name, IReadOnlyList<CustodianPositionItem> Positions, string? IncomeAccountNumber = null);

public sealed record IncomeAccountRequest(string? IncomeAccountNumber);

public sealed record IncomeModeRequest(string? Mode);

public sealed record PayDividendRequest(decimal? Amount);

public sealed record CustodianDividendPaymentItem(
    string Id, string CorporateActionId, string AccountNumber, decimal EntitledPosition, decimal? ElectedQuantity, decimal? Shares,
    decimal? Cash, string? IncomeAccountNumber, string Status, string? BankLineId, string? MovementId);

public sealed record CustodianDividendPaymentPage(IReadOnlyList<CustodianDividendPaymentItem> Items, int Page, int PageSize, int TotalCount);

public sealed record CustodianAccountList(IReadOnlyList<CustodianAccountItem> Items);

public sealed record CustodianControlMovementItem(
    string Id, string Direction, string Kind, string Symbol, string Currency, decimal Quantity, string SettlementDate,
    string? InstructionId, string? TradeReference, string? CorporateActionId, string? CounterpartyName, string? CounterpartyAccount,
    string? Reference, bool OmitFromFeed);

public sealed record CustodianMovementPage(IReadOnlyList<CustodianControlMovementItem> Items, int Page, int PageSize, int TotalCount);

public sealed record CustodianTransferItem(
    string TransferId, string AccountNumber, string InstructionId, string Status, string ModeAtReceipt, string? RejectReason,
    string? MovementId, DateTimeOffset ReceivedAt);

public sealed record CustodianTransferPage(IReadOnlyList<CustodianTransferItem> Items, int Page, int PageSize, int TotalCount);

public sealed record OpenCustodianAccountRequest(string? AccountNumber, string? Currency, string? Name, string? IncomeAccountNumber = null);

public sealed record TransferModeRequest(string? Mode, string? Reason);

public sealed record RoundingModeRequest(string? Mode);

public sealed record ExecuteTransferRequest(decimal? Quantity);

public sealed record AddCustodianMovementRequest(
    string? AccountNumber, string? Direction, string? Symbol, decimal? Quantity, string? CounterpartyName, string? CounterpartyAccount,
    string? Reference, bool? OmitFromFeed);
