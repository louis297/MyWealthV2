using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.TradeSettlements.Queries;

/// <summary>Trade-settlement R16: the trade settlements of the caller's Tenant, newest first.</summary>
[Authorize(Policy = Policies.TradeSettlementsManage)]
public sealed record GetTradeSettlementsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? ClientMoneyAccountId = null,
    bool? Open = null,
    Guid? TenantId = null) : IRequest<PagedList<TradeSettlementDto>>
{
    internal static IReadOnlyList<string> Names(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Trade-settlement R16: one trade settlement with its fills and payments.</summary>
[Authorize(Policy = Policies.TradeSettlementsManage)]
public sealed record GetTradeSettlementByIdQuery(Guid Id) : IRequest<TradeSettlementDto>;

public class GetTradeSettlementsQueryValidator : AbstractValidator<GetTradeSettlementsQuery>
{
    private static readonly string[] Statuses = Enum.GetNames<TradeSettlementStatus>();

    public GetTradeSettlementsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetTradeSettlementsQuery.Names(status).All(name => Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Statuses)}, comma-separated.");
    }
}

public class GetTradeSettlementsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetTradeSettlementsQuery, PagedList<TradeSettlementDto>>
{
    public async Task<PagedList<TradeSettlementDto>> Handle(GetTradeSettlementsQuery request, CancellationToken cancellationToken)
    {
        var query = TradeSettlementReader.Scope(db, currentUser);
        var statuses = GetTradeSettlementsQuery.Names(request.Status)
            .Select(name => Enum.Parse<TradeSettlementStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        if (request.ClientMoneyAccountId is Guid clientMoneyId)
        {
            query = query.Where(row => db.ClientMoneyAccounts.Any(account => account.Id == row.ClientMoneyAccountId && account.PublicId == clientMoneyId));
        }

        if (request.Open == true)
        {
            query = query.Where(row => TradeSettlementReader.OpenStatuses.Contains(row.Status));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedList<TradeSettlementDto>
        {
            Items = await TradeSettlementReader.ToDtosAsync(db, rows, detail: false, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetTradeSettlementByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetTradeSettlementByIdQuery, TradeSettlementDto>
{
    public async Task<TradeSettlementDto> Handle(GetTradeSettlementByIdQuery request, CancellationToken cancellationToken)
    {
        var row = await TradeSettlementReader.Scope(db, currentUser)
                      .SingleOrDefaultAsync(item => item.PublicId == request.Id, cancellationToken)
                  ?? throw new NotFoundException("TradeSettlement", request.Id);
        return (await TradeSettlementReader.ToDtosAsync(db, [row], detail: true, cancellationToken)).Single();
    }
}

/// <summary>Reads trade settlements of the caller's Tenant and maps them to <see cref="TradeSettlementDto"/>.</summary>
internal static class TradeSettlementReader
{
    public static readonly TradeSettlementStatus[] OpenStatuses =
    [
        TradeSettlementStatus.AwaitingPayment, TradeSettlementStatus.AwaitingReceipt, TradeSettlementStatus.PaymentFailed,
        TradeSettlementStatus.Disputed
    ];

    public static IQueryable<TradeSettlement> Scope(IApplicationDbContext db, ICurrentUser currentUser)
    {
        var tenantId = currentUser.TenantId ?? throw new Common.Exceptions.ForbiddenAccessException();
        return db.TradeSettlements.AsNoTracking().Where(row => row.TenantId == tenantId);
    }

    public static async Task<IReadOnlyList<TradeSettlementDto>> ToDtosAsync(
        IApplicationDbContext db,
        IReadOnlyList<TradeSettlement> rows,
        bool detail,
        CancellationToken cancellationToken)
    {
        var ids = rows.Select(row => row.Id).ToList();
        var clientMoneyIds = rows.Select(row => row.ClientMoneyAccountId).Distinct().ToList();
        var clientMoney = await db.ClientMoneyAccounts.AsNoTracking()
            .Where(row => clientMoneyIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new TradeSettlementClientMoneyDto(row.PublicId, row.Currency.Trim(), row.AccountNumber), cancellationToken);
        var userIds = rows.Where(row => row.SetAsideByUserId is not null).Select(row => row.SetAsideByUserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);
        var lines = (await db.BankStatementLines.AsNoTracking()
                .Where(row => row.TradeSettlementId != null && ids.Contains(row.TradeSettlementId.Value)
                              && row.MatchMethod == StatementLineMatchMethod.Settlement)
                .Select(row => new { TradeSettlementId = row.TradeSettlementId!.Value, row.Id, row.PublicId })
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.TradeSettlementId)
            .ToDictionary(group => group.Key, group => group.MaxBy(row => row.Id)!.PublicId);

        var payments = new Dictionary<int, List<TradeSettlementPaymentDto>>();
        var fills = new Dictionary<int, List<TradeSettlementFillDto>>();
        if (detail)
        {
            payments = (await db.OutgoingPayments.AsNoTracking()
                    .Where(row => row.TradeSettlementId != null && ids.Contains(row.TradeSettlementId.Value))
                    .OrderBy(row => row.Id)
                    .ToListAsync(cancellationToken))
                .GroupBy(row => row.TradeSettlementId!.Value)
                .ToDictionary(group => group.Key, group => group
                    .Select(row => new TradeSettlementPaymentDto(row.Status.ToString(), row.Attempts, row.LastFailure?.ToString(), row.Created))
                    .ToList());
            fills = (await (
                    from fill in db.OrderFills.AsNoTracking()
                    join order in db.Orders.AsNoTracking() on fill.OrderId equals order.Id
                    join account in db.Accounts.AsNoTracking() on order.AccountId equals account.Id
                    where fill.TradeSettlementId != null && ids.Contains(fill.TradeSettlementId.Value)
                    orderby fill.Id
                    select new
                    {
                        TradeSettlementId = fill.TradeSettlementId!.Value,
                        fill.PublicId,
                        OrderPublicId = order.PublicId,
                        AccountPublicId = account.PublicId,
                        order.Side,
                        fill.Quantity,
                        fill.Price,
                        fill.Status
                    })
                    .ToListAsync(cancellationToken))
                .GroupBy(row => row.TradeSettlementId)
                .ToDictionary(group => group.Key, group => group
                    .Select(row => new TradeSettlementFillDto(
                        row.PublicId, row.OrderPublicId, row.AccountPublicId, row.Side.ToString(), row.Quantity, row.Price,
                        decimal.Round(row.Quantity * row.Price, 4, MidpointRounding.AwayFromZero), row.Status.ToString()))
                    .ToList());
        }

        return rows.Select(row => new TradeSettlementDto
        {
            Id = row.PublicId,
            ClientMoneyAccount = clientMoney[row.ClientMoneyAccountId],
            Connection = row.Connection,
            BrokerSettlementId = row.BrokerSettlementId,
            SettledDate = row.SettledDate,
            FillCount = row.FillCount,
            NetAmount = row.NetAmount,
            Broker = new TradeSettlementBrokerDto(row.BrokerFillCount, row.BrokerNetAmount, row.BrokerSettlementAccount),
            Status = row.Status.ToString(),
            Failure = row.Failure?.ToString(),
            FailureDetail = row.FailureDetail,
            CompletedAt = row.CompletedAt,
            StatementLineId = lines.TryGetValue(row.Id, out var line) ? line : null,
            SetAside = row.SetAsideAt is { } at
                ? new TradeSettlementSetAsideDto(users.GetValueOrDefault(row.SetAsideByUserId!.Value), at, row.SetAsideNote!)
                : null,
            Payments = detail ? payments.GetValueOrDefault(row.Id) ?? [] : null,
            Fills = detail ? fills.GetValueOrDefault(row.Id) ?? [] : null,
            RowVersion = Convert.ToBase64String(row.RowVersion ?? [])
        }).ToList();
    }
}
