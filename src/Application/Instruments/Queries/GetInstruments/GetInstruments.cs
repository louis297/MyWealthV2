using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Instruments.Queries.GetInstruments;

[Authorize(Policy = Policies.InstrumentsRead)]
public record GetInstrumentsQuery(
    int Page = 1,
    int PageSize = 20,
    string? EnabledOnly = null,
    string? Search = null,
    Guid? TenantId = null) : IRequest<PagedList<InstrumentDto>>;

public class GetInstrumentsQueryValidator : AbstractValidator<GetInstrumentsQuery>
{
    public GetInstrumentsQueryValidator(ICurrentUser currentUser)
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.EnabledOnly)
            .Must(value => value is null || bool.TryParse(value, out _))
            .WithMessage("enabledOnly must be true or false.");

        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId)
                .NotEmpty()
                .WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

public class GetInstrumentsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetInstrumentsQuery, PagedList<InstrumentDto>>
{
    public async Task<PagedList<InstrumentDto>> Handle(
        GetInstrumentsQuery request,
        CancellationToken cancellationToken)
    {
        int tenantId;
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            var tenant = await db.Tenants.AsNoTracking()
                             .SingleOrDefaultAsync(row => row.PublicId == request.TenantId, cancellationToken)
                         ?? throw new NotFoundException("Tenant", request.TenantId!);
            tenantId = tenant.Id;
        }
        else
        {
            tenantId = currentUser.TenantId
                       ?? throw new NotFoundException("Tenant", "current");
        }

        var query =
            from instrument in db.Instruments.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on instrument.TenantId equals tenant.Id
            where instrument.TenantId == tenantId
            select new { instrument, tenant };

        if (bool.TryParse(request.EnabledOnly, out var enabledOnly) && enabledOnly)
        {
            query = query.Where(row => row.instrument.IsEnabled);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            if (Guid.TryParse(search, out var publicId))
            {
                query = query.Where(row =>
                    row.instrument.PublicId == publicId
                    || row.instrument.Symbol.Contains(search)
                    || row.instrument.Name.Contains(search));
            }
            else
            {
                query = query.Where(row =>
                    row.instrument.Symbol.Contains(search)
                    || row.instrument.Name.Contains(search));
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(row => row.instrument.Symbol)
            .ThenBy(row => row.instrument.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<InstrumentDto>
        {
            Items = items.Select(row => InstrumentDto.From(row.instrument, row.tenant)).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
