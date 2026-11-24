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
        return control;
    }

    private static async Task<IResult> GetSettingsAsync(SimulatorDbContext db, TimeProvider time, BrokerBankAccounts brokerAccounts,
        CancellationToken cancellationToken)
    {
        await BrokerBook.UpToDateAsync(db, time, brokerAccounts, cancellationToken);
        var settings = await db.CustodianSettings.AsNoTracking().SingleAsync(cancellationToken);
        return Results.Ok(new CustodianSettingsItem(settings.TransferMode.ToString(), settings.RejectReason, settings.RoundingMode.ToString()));
    }

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
                positions.Select(position => new CustodianPositionItem(position.Symbol, account.Currency.Trim(), position.Quantity)).ToList()));
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

        var number = body.AccountNumber!.Trim();
        if (await db.CustodianAccounts.AnyAsync(row => row.AccountNumber == number, http.RequestAborted))
        {
            return ControlEndpoints.Refused("AccountNumber", "An account with this number is already open.");
        }

        var world = await db.Worlds.AsNoTracking().SingleAsync(http.RequestAborted);
        var realNow = time.GetUtcNow();
        var account = CustodianAccount.Open(number, body.Currency!, body.Name!.Trim(), world.Now(realNow));
        db.CustodianAccounts.Add(account);
        Log(db, http, world, realNow, ControlActions.CustodianAccountOpened, body);
        await db.SaveChangesAsync(http.RequestAborted);
        return Results.Created($"/control/custodian/accounts/{Uri.EscapeDataString(account.AccountNumber)}",
            new CustodianAccountItem(account.AccountNumber, account.Currency.Trim(), account.Name, []));
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
        return Results.Ok(new CustodianSettingsItem(settings.TransferMode.ToString(), settings.RejectReason, settings.RoundingMode.ToString()));
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
        return Results.Ok(new CustodianSettingsItem(settings.TransferMode.ToString(), settings.RejectReason, settings.RoundingMode.ToString()));
    }

    private static void Log(SimulatorDbContext db, HttpContext http, World world, DateTimeOffset realNow, string action, object body) =>
        db.ControlLog.Add(ControlLogEntry.Create(
            realNow, world.Now(realNow), ControlEndpoints.Actor(http.User), action,
            JsonSerializer.Serialize(body, body.GetType(), ControlEndpoints.DetailJson)));
}

public sealed record CustodianSettingsItem(string TransferMode, string? RejectReason, string RoundingMode);

public sealed record CustodianAccountItem(string AccountNumber, string Currency, string Name, IReadOnlyList<CustodianPositionItem> Positions);

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

public sealed record OpenCustodianAccountRequest(string? AccountNumber, string? Currency, string? Name);

public sealed record TransferModeRequest(string? Mode, string? Reason);

public sealed record RoundingModeRequest(string? Mode);

public sealed record ExecuteTransferRequest(decimal? Quantity);

public sealed record AddCustodianMovementRequest(
    string? AccountNumber, string? Direction, string? Symbol, decimal? Quantity, string? CounterpartyName, string? CounterpartyAccount,
    string? Reference, bool? OmitFromFeed);
