using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>Corporate-actions R34: the Tenant's corporate actions, latest ex-date first.</summary>
[Authorize(Policy = Policies.CorporateActionsRead)]
public sealed record GetCorporateActionsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? InstrumentId = null,
    string? Origin = null,
    string? TenantId = null) : IRequest<PagedList<CorporateActionDto>>
{
    internal static IReadOnlyList<string> Names(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Corporate-actions R35: one corporate action of the Tenant.</summary>
[Authorize(Policy = Policies.CorporateActionsRead)]
public sealed record GetCorporateActionByIdQuery(Guid Id) : IRequest<CorporateActionDto>;

public class GetCorporateActionsQueryValidator : AbstractValidator<GetCorporateActionsQuery>
{
    private static readonly string[] Statuses = Enum.GetNames<CorporateActionStatus>();
    private static readonly string[] Origins = Enum.GetNames<CorporateActionOrigin>();

    public GetCorporateActionsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
        RuleFor(query => query.Status)
            .Must(status => GetCorporateActionsQuery.Names(status).All(name => Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Statuses)}, comma-separated.");
        RuleFor(query => query.Origin)
            .Must(origin => origin is null || Origins.Contains(origin.Trim(), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Origin must be one of {string.Join(", ", Origins)}.");
    }
}

public class GetCorporateActionsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCorporateActionsQuery, PagedList<CorporateActionDto>>
{
    public async Task<PagedList<CorporateActionDto>> Handle(GetCorporateActionsQuery request, CancellationToken cancellationToken)
    {
        var query = CorporateActionReader.Scope(db, currentUser).AsNoTracking();
        var statuses = GetCorporateActionsQuery.Names(request.Status)
            .Select(name => Enum.Parse<CorporateActionStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(action => statuses.Contains(action.Status));
        }

        if (request.InstrumentId is Guid instrumentId)
        {
            query = query.Where(action => db.Instruments.Any(instrument =>
                instrument.Id == action.InstrumentId && instrument.PublicId == instrumentId));
        }

        if (request.Origin is not null)
        {
            var origin = Enum.Parse<CorporateActionOrigin>(request.Origin.Trim(), ignoreCase: true);
            query = query.Where(action => action.Origin == origin);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(action => action.ExDate)
            .ThenByDescending(action => action.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<CorporateActionDto>
        {
            Items = await CorporateActionReader.ToDtosAsync(db, currentUser, rows, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetCorporateActionByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCorporateActionByIdQuery, CorporateActionDto>
{
    public async Task<CorporateActionDto> Handle(GetCorporateActionByIdQuery request, CancellationToken cancellationToken)
    {
        var action = await CorporateActionReader.Scope(db, currentUser).AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                     ?? throw new Common.Exceptions.NotFoundException("CorporateAction", request.Id);
        return (await CorporateActionReader.ToDtosAsync(db, currentUser, [action], cancellationToken)).Single();
    }
}
