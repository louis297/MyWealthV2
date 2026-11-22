using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.AccountClosures.Queries;

/// <summary>Account closure R26: the closures of one Account in the caller's scope, newest requested first.</summary>
[Authorize(Policy = Policies.AccountClosuresRead)]
public sealed record GetAccountClosuresQuery(
    Guid AccountId,
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? TenantId = null) : IRequest<PagedList<AccountClosureDto>>
{
    internal static IReadOnlyList<string> StatusNames(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? []
            : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Account closure R26: one closure of the Account of the path.</summary>
[Authorize(Policy = Policies.AccountClosuresRead)]
public sealed record GetAccountClosureByIdQuery(Guid AccountId, Guid Id) : IRequest<AccountClosureDto>;

public class GetAccountClosuresQueryValidator : AbstractValidator<GetAccountClosuresQuery>
{
    private static readonly string[] Names = Enum.GetNames<AccountClosureStatus>();

    public GetAccountClosuresQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetAccountClosuresQuery.StatusNames(status)
                .All(name => Names.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Names)}, comma-separated.");
    }
}

public class GetAccountClosuresQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAccountClosuresQuery, PagedList<AccountClosureDto>>
{
    public async Task<PagedList<AccountClosureDto>> Handle(GetAccountClosuresQuery request, CancellationToken cancellationToken)
    {
        var accountId = await AccountClosureReader.AccountIdAsync(db, currentUser, request.AccountId, cancellationToken);
        var query = db.AccountClosures.AsNoTracking().Where(row => row.AccountId == accountId);
        var statuses = GetAccountClosuresQuery.StatusNames(request.Status)
            .Select(name => Enum.Parse<AccountClosureStatus>(name, ignoreCase: true))
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

        return new PagedList<AccountClosureDto>
        {
            Items = await AccountClosureReader.ToDtosAsync(db, currentUser, rows, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetAccountClosureByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAccountClosureByIdQuery, AccountClosureDto>
{
    public async Task<AccountClosureDto> Handle(GetAccountClosureByIdQuery request, CancellationToken cancellationToken)
    {
        var accountId = await AccountClosureReader.AccountIdAsync(db, currentUser, request.AccountId, cancellationToken);
        // R1: a closure id that is not a closure of this Account is 404.
        var row = await db.AccountClosures.AsNoTracking()
                      .SingleOrDefaultAsync(item => item.PublicId == request.Id && item.AccountId == accountId, cancellationToken)
                  ?? throw new NotFoundException("Account closure", request.Id);
        return (await AccountClosureReader.ToDtosAsync(db, currentUser, [row], cancellationToken)).Single();
    }
}
