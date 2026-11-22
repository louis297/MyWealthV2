using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Orders.Queries;

/// <summary>Orders R16: the orders of the Accounts in the caller's scope, newest placed first, without their fills.</summary>
[Authorize(Policy = Policies.OrdersRead)]
public sealed record GetOrdersQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? AccountId = null,
    Guid? CustomerId = null,
    Guid? InstrumentId = null,
    string? Side = null,
    string? Status = null,
    bool? PendingFill = null,
    Guid? TenantId = null) : IRequest<PagedList<OrderDto>>
{
    internal static IReadOnlyList<string> Names(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Orders R16: one order in the caller's scope, with its fills.</summary>
[Authorize(Policy = Policies.OrdersRead)]
public sealed record GetOrderByIdQuery(Guid Id) : IRequest<OrderDto>;

public class GetOrdersQueryValidator : AbstractValidator<GetOrdersQuery>
{
    private static readonly string[] Statuses = Enum.GetNames<OrderStatus>();
    private static readonly string[] Sides = Enum.GetNames<OrderSide>();

    public GetOrdersQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetOrdersQuery.Names(status).All(name => Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Statuses)}, comma-separated.");
        RuleFor(query => query.Side)
            .Must(side => string.IsNullOrWhiteSpace(side) || Sides.Contains(side.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage("Side must be Buy or Sell.");
    }
}

public class GetOrdersQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetOrdersQuery, PagedList<OrderDto>>
{
    public async Task<PagedList<OrderDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = OrderReader.Scope(db, currentUser).AsNoTracking();
        if (request.AccountId is Guid accountId)
        {
            query = query.Where(row => db.Accounts.Any(account => account.Id == row.AccountId && account.PublicId == accountId));
        }

        if (request.CustomerId is Guid customerId)
        {
            query = query.Where(row => db.Accounts.Any(account => account.Id == row.AccountId
                && db.Users.Any(user => user.Id == account.CustomerId && user.PublicId == customerId)));
        }

        if (request.InstrumentId is Guid instrumentId)
        {
            query = query.Where(row => db.Instruments.Any(instrument => instrument.Id == row.InstrumentId && instrument.PublicId == instrumentId));
        }

        if (!string.IsNullOrWhiteSpace(request.Side))
        {
            var side = Enum.Parse<OrderSide>(request.Side.Trim(), ignoreCase: true);
            query = query.Where(row => row.Side == side);
        }

        var statuses = GetOrdersQuery.Names(request.Status)
            .Select(name => Enum.Parse<OrderStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        // R16: only orders with a fill that is not posted yet.
        if (request.PendingFill == true)
        {
            query = query.Where(row => db.OrderFills.Any(fill => fill.OrderId == row.Id && fill.Status == OrderFillStatus.Pending));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.PlacedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<OrderDto>
        {
            Items = await OrderReader.ToDtosAsync(db, currentUser, rows, withFills: false, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetOrderByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetOrderByIdQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var row = await OrderReader.Scope(db, currentUser).AsNoTracking()
                      .SingleOrDefaultAsync(item => item.PublicId == request.Id, cancellationToken)
                  ?? throw new NotFoundException("Order", request.Id);
        return (await OrderReader.ToDtosAsync(db, currentUser, [row], withFills: true, cancellationToken)).Single();
    }
}
