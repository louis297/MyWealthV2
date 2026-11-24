using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.CustodyMovements;

/// <summary>A custody movement item (docs/features/in-kind-transfers.md R19).</summary>
public sealed class CustodyMovementDto
{
    public required Guid Id { get; init; }

    public required Guid SafekeepingAccountId { get; init; }

    public required string Direction { get; init; }

    public string? Kind { get; init; }

    public required string Symbol { get; init; }

    public required string Currency { get; init; }

    public CustodyMovementInstrumentDto? Instrument { get; init; }

    public required decimal Quantity { get; init; }

    public required string SettlementDate { get; init; }

    public string? InstructionId { get; init; }

    public string? TradeReference { get; init; }

    public string? CorporateActionReference { get; init; }

    public string? CounterpartyName { get; init; }

    public string? CounterpartyAccount { get; init; }

    public string? Reference { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    public required string Status { get; init; }

    public string? UnmatchedReason { get; init; }

    public string? Method { get; init; }

    public Guid? AccountId { get; init; }

    public Guid? TransactionId { get; init; }

    public Guid? InKindTransferId { get; init; }

    public Guid? OrderId { get; init; }

    public Guid? FillId { get; init; }

    public Guid? CorporateActionId { get; init; }

    public CustodyMovementActionDto? LastAction { get; init; }

    public required string RowVersion { get; init; }
}

public sealed record CustodyMovementInstrumentDto(Guid Id, string Symbol, string Name);

public sealed record CustodyMovementActionDto(string Action, PersonRefDto By, DateTimeOffset At, string? Note);

/// <summary>R19: the Tenant's custody movements, newest received first.</summary>
[Authorize(Policy = Policies.CustodyMovementsManage)]
public sealed record GetCustodyMovementsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    string? Direction = null,
    string? Kind = null,
    Guid? InstrumentId = null,
    Guid? SafekeepingAccountId = null,
    Guid? TenantId = null) : IRequest<PagedList<CustodyMovementDto>>
{
    internal static IReadOnlyList<string> StatusNames(string? status) =>
        string.IsNullOrWhiteSpace(status) ? [] : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

[Authorize(Policy = Policies.CustodyMovementsManage)]
public sealed record GetCustodyMovementByIdQuery(Guid Id) : IRequest<CustodyMovementDto>;

public class GetCustodyMovementsQueryValidator : AbstractValidator<GetCustodyMovementsQuery>
{
    private static readonly string[] Names = Enum.GetNames<CustodyMovementStatus>();

    public GetCustodyMovementsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetCustodyMovementsQuery.StatusNames(status).All(name => Names.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Names)}, comma-separated.");
        RuleFor(query => query.Direction)
            .Must(value => value is null || Enum.TryParse<CustodyMovementDirection>(value, out _))
            .WithMessage("Direction must be In or Out.");
        RuleFor(query => query.Kind)
            .Must(value => value is null || Enum.TryParse<CustodyMovementKind>(value, out _))
            .WithMessage("Kind must be Transfer, Trade or CorporateAction.");
    }
}

public class GetCustodyMovementsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetCustodyMovementsQuery, PagedList<CustodyMovementDto>>
{
    public async Task<PagedList<CustodyMovementDto>> Handle(GetCustodyMovementsQuery request, CancellationToken cancellationToken)
    {
        var query = db.CustodyMovements.AsNoTracking();
        var statuses = GetCustodyMovementsQuery.StatusNames(request.Status)
            .Select(name => Enum.Parse<CustodyMovementStatus>(name, ignoreCase: true)).Distinct().ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        if (request.Direction is not null)
        {
            var direction = Enum.Parse<CustodyMovementDirection>(request.Direction);
            query = query.Where(row => row.Direction == direction);
        }

        if (request.Kind is not null)
        {
            var kind = Enum.Parse<CustodyMovementKind>(request.Kind);
            query = query.Where(row => row.Kind == kind);
        }

        if (request.InstrumentId is Guid instrumentId)
        {
            query = query.Where(row => db.Instruments.Any(instrument => instrument.Id == row.InstrumentId && instrument.PublicId == instrumentId));
        }

        if (request.SafekeepingAccountId is Guid safekeepingId)
        {
            query = query.Where(row => db.SafekeepingAccounts.Any(account => account.Id == row.SafekeepingAccountId && account.PublicId == safekeepingId));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(row => row.ReceivedAt).ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        return new PagedList<CustodyMovementDto>
        {
            Items = await CustodyMovementReader.ToDtosAsync(db, rows, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetCustodyMovementByIdQueryHandler(IApplicationDbContext db) : IRequestHandler<GetCustodyMovementByIdQuery, CustodyMovementDto>
{
    public async Task<CustodyMovementDto> Handle(GetCustodyMovementByIdQuery request, CancellationToken cancellationToken)
    {
        var row = await db.CustodyMovements.AsNoTracking().SingleOrDefaultAsync(item => item.PublicId == request.Id, cancellationToken)
                  ?? throw new NotFoundException("Custody movement", request.Id);
        return (await CustodyMovementReader.ToDtosAsync(db, [row], cancellationToken)).Single();
    }
}

internal static class CustodyMovementReader
{
    public static async Task<List<CustodyMovementDto>> ToDtosAsync(
        IApplicationDbContext db, IReadOnlyList<CustodyMovement> rows, CancellationToken cancellationToken)
    {
        var safekeepingIds = rows.Select(row => row.SafekeepingAccountId).Distinct().ToList();
        var instrumentIds = rows.Where(row => row.InstrumentId is not null).Select(row => row.InstrumentId!.Value).Distinct().ToList();
        var accountIds = rows.Where(row => row.AccountId is not null).Select(row => row.AccountId!.Value).Distinct().ToList();
        var transactionIds = rows.Where(row => row.TransactionId is not null).Select(row => row.TransactionId!.Value).Distinct().ToList();
        var transferIds = rows.Where(row => row.InKindTransferId is not null).Select(row => row.InKindTransferId!.Value).Distinct().ToList();
        var fillIds = rows.Where(row => row.OrderFillId is not null).Select(row => row.OrderFillId!.Value).Distinct().ToList();
        var actionIds = rows.Where(row => row.CorporateActionId is not null).Select(row => row.CorporateActionId!.Value).Distinct().ToList();
        var userIds = rows.Where(row => row.LastActionByUserId is not null).Select(row => row.LastActionByUserId!.Value).Distinct().ToList();

        var safekeeping = await db.SafekeepingAccounts.AsNoTracking().Where(row => safekeepingIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var instruments = await db.Instruments.AsNoTracking().Where(row => instrumentIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new CustodyMovementInstrumentDto(row.PublicId, row.Symbol, row.Name), cancellationToken);
        var accounts = await db.Accounts.AsNoTracking().Where(row => accountIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var transactions = await db.Transactions.AsNoTracking().Where(row => transactionIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var transfers = await db.InKindTransfers.AsNoTracking().Where(row => transferIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var fills = await (
                from fill in db.OrderFills.AsNoTracking()
                join order in db.Orders.AsNoTracking() on fill.OrderId equals order.Id
                where fillIds.Contains(fill.Id)
                select new { fill.Id, fill.PublicId, OrderPublicId = order.PublicId })
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var actions = await db.CorporateActions.AsNoTracking().Where(row => actionIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var users = await db.Users.AsNoTracking().Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);

        return rows.Select(row => new CustodyMovementDto
        {
            Id = row.PublicId,
            SafekeepingAccountId = safekeeping[row.SafekeepingAccountId],
            Direction = row.Direction.ToString(),
            Kind = row.Kind?.ToString(),
            Symbol = row.Symbol,
            Currency = row.Currency.Trim(),
            Instrument = row.InstrumentId is int instrument ? instruments.GetValueOrDefault(instrument) : null,
            Quantity = row.Quantity,
            SettlementDate = row.SettlementDate.ToString("yyyy-MM-dd"),
            InstructionId = row.InstructionId,
            TradeReference = row.TradeReference,
            CorporateActionReference = row.CorporateActionReference,
            CounterpartyName = row.CounterpartyName,
            CounterpartyAccount = row.CounterpartyAccount,
            Reference = row.Reference,
            ReceivedAt = row.ReceivedAt,
            Status = row.Status.ToString(),
            UnmatchedReason = row.UnmatchedReason?.ToString(),
            Method = row.MatchMethod?.ToString(),
            AccountId = row.AccountId is int account ? accounts[account] : null,
            TransactionId = row.TransactionId is int transaction ? transactions[transaction] : null,
            InKindTransferId = row.InKindTransferId is int transfer ? transfers[transfer] : null,
            OrderId = row.OrderFillId is long orderFill ? fills[orderFill].OrderPublicId : null,
            FillId = row.OrderFillId is long fillId ? fills[fillId].PublicId : null,
            CorporateActionId = row.CorporateActionId is int action ? actions[action] : null,
            LastAction = row.LastAction is { } last && row.LastActionByUserId is int by
                ? new CustodyMovementActionDto(last.ToString(), users[by], row.LastActionAt!.Value, row.LastActionNote)
                : null,
            RowVersion = Convert.ToBase64String(row.RowVersion ?? [])
        }).ToList();
    }
}

/// <summary>R18: the body of ignore and reopen.</summary>
public abstract class CustodyMovementActionCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>R18: a TenantAdmin has dealt with a movement in suspense.</summary>
[Authorize(Policy = Policies.CustodyMovementsManage)]
public sealed class IgnoreCustodyMovementCommand : CustodyMovementActionCommand;

/// <summary>R18: an ignored movement is in suspense again.</summary>
[Authorize(Policy = Policies.CustodyMovementsManage)]
public sealed class ReopenCustodyMovementCommand : CustodyMovementActionCommand;

public class IgnoreCustodyMovementCommandValidator : AbstractValidator<IgnoreCustodyMovementCommand>
{
    public IgnoreCustodyMovementCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(note => note?.Trim().Length is >= 1 and <= CustodyMovement.NoteMaxLength)
            .WithMessage("Note must be 1 to 200 characters.");
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class ReopenCustodyMovementCommandValidator : AbstractValidator<ReopenCustodyMovementCommand>
{
    public ReopenCustodyMovementCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(note => (note?.Trim().Length ?? 0) <= CustodyMovement.NoteMaxLength)
            .WithMessage("Note must be 1 to 200 characters.");
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class IgnoreCustodyMovementCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<IgnoreCustodyMovementCommand>
{
    public Task Handle(IgnoreCustodyMovementCommand request, CancellationToken cancellationToken) =>
        CustodyMovementChange.ApplyAsync(db, currentUser, request,
            (movement, actor) => movement.Ignore(actor, time.GetUtcNow(), request.Note!), cancellationToken);
}

public class ReopenCustodyMovementCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<ReopenCustodyMovementCommand>
{
    public Task Handle(ReopenCustodyMovementCommand request, CancellationToken cancellationToken) =>
        CustodyMovementChange.ApplyAsync(db, currentUser, request,
            (movement, actor) => movement.Reopen(actor, time.GetUtcNow(), request.Note), cancellationToken);
}

internal static class CustodyMovementChange
{
    public static async Task ApplyAsync(
        IApplicationDbContext db, ICurrentUser currentUser, CustodyMovementActionCommand request,
        Action<CustodyMovement, int> change, CancellationToken cancellationToken)
    {
        var movement = await db.CustodyMovements.SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                       ?? throw new NotFoundException("Custody movement", request.Id);
        if (!string.Equals(Convert.ToBase64String(movement.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        var actor = currentUser.Person?.Id ?? throw new ForbiddenAccessException();
        try
        {
            change(movement, actor);
        }
        catch (CustodyMovementStatusException exception)
        {
            throw new ValidationException([new ValidationFailure("Status", exception.Message)]);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }
}
