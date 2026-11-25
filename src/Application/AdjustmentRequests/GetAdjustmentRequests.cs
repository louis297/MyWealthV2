using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.AdjustmentRequests;

/// <summary>Adjustment review R20: the Tenant's adjustment requests, newest requested first.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed record GetAdjustmentRequestsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? AccountId = null,
    Guid? CustomerId = null,
    Guid? TenantId = null) : IRequest<PagedList<AdjustmentRequestDto>>
{
    internal static IReadOnlyList<string> StatusNames(string? status) =>
        string.IsNullOrWhiteSpace(status) ? [] : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Adjustment review R20: one adjustment request of the Tenant.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed record GetAdjustmentRequestByIdQuery(Guid Id) : IRequest<AdjustmentRequestDto>;

public class GetAdjustmentRequestsQueryValidator : AbstractValidator<GetAdjustmentRequestsQuery>
{
    private static readonly string[] Names = Enum.GetNames<AdjustmentRequestStatus>();

    public GetAdjustmentRequestsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetAdjustmentRequestsQuery.StatusNames(status).All(name => Names.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Names)}, comma-separated.");
    }
}

public class GetAdjustmentRequestsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAdjustmentRequestsQuery, PagedList<AdjustmentRequestDto>>
{
    public async Task<PagedList<AdjustmentRequestDto>> Handle(GetAdjustmentRequestsQuery request, CancellationToken cancellationToken)
    {
        var query = AdjustmentRequestReader.Scope(db, currentUser).AsNoTracking();
        if (request.AccountId is Guid accountId)
        {
            query = query.Where(row => db.Accounts.Any(account => account.Id == row.AccountId && account.PublicId == accountId));
        }

        if (request.CustomerId is Guid customerId)
        {
            query = query.Where(row => db.Accounts.Any(account => account.Id == row.AccountId
                && db.Users.Any(user => user.Id == account.CustomerId && user.PublicId == customerId)));
        }

        var statuses = GetAdjustmentRequestsQuery.StatusNames(request.Status)
            .Select(name => Enum.Parse<AdjustmentRequestStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.RequestedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<AdjustmentRequestDto>
        {
            Items = await AdjustmentRequestReader.ToDtosAsync(db, rows, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetAdjustmentRequestByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAdjustmentRequestByIdQuery, AdjustmentRequestDto>
{
    public async Task<AdjustmentRequestDto> Handle(GetAdjustmentRequestByIdQuery request, CancellationToken cancellationToken)
    {
        var row = await AdjustmentRequestReader.Scope(db, currentUser).AsNoTracking()
                      .SingleOrDefaultAsync(item => item.PublicId == request.Id, cancellationToken)
                  ?? throw new NotFoundException("Adjustment request", request.Id);
        return (await AdjustmentRequestReader.ToDtosAsync(db, [row], cancellationToken)).Single();
    }
}
