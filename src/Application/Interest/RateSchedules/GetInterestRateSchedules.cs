using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Interest.RateSchedules;

/// <summary>Interest-run R15: the current tenant's schedules, by currency then effective date.</summary>
[Authorize(Policy = Policies.InterestRatesRead)]
public record GetInterestRateSchedulesQuery(int Page = 1, int PageSize = 20, string? Currency = null, string? TenantId = null)
    : IRequest<PagedList<InterestRateScheduleDto>>;

public class GetInterestRateSchedulesQueryValidator : AbstractValidator<GetInterestRateSchedulesQuery>
{
    public GetInterestRateSchedulesQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
    }
}

public class GetInterestRateSchedulesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<GetInterestRateSchedulesQuery, PagedList<InterestRateScheduleDto>>
{
    public async Task<PagedList<InterestRateScheduleDto>> Handle(GetInterestRateSchedulesQuery request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId!.Value;
        var query = db.InterestRateSchedules.AsNoTracking().Where(schedule => schedule.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            var code = request.Currency.Trim().ToUpperInvariant();
            query = query.Where(schedule => schedule.Currency == code);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var schedules = await query
            .Include(schedule => schedule.BandRows)
            .OrderBy(schedule => schedule.Currency)
            .ThenBy(schedule => schedule.EffectiveFrom)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var cutOffs = await InterestCutOff.ForAsync(
            db, tenantId, schedules.Select(schedule => schedule.Currency).ToList(), InterestCutOff.Today(time), cancellationToken);

        return new PagedList<InterestRateScheduleDto>
        {
            Items = schedules.Select(schedule => InterestRateScheduleDto.From(schedule, cutOffs[schedule.Currency])).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

/// <summary>Interest-run R15: one schedule of the current tenant.</summary>
[Authorize(Policy = Policies.InterestRatesRead)]
public record GetInterestRateScheduleQuery(Guid Id) : IRequest<InterestRateScheduleDto>;

public class GetInterestRateScheduleQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<GetInterestRateScheduleQuery, InterestRateScheduleDto>
{
    public async Task<InterestRateScheduleDto> Handle(GetInterestRateScheduleQuery request, CancellationToken cancellationToken)
    {
        var schedule = await InterestRateScheduleRules.LoadAsync(db, currentUser, request.Id, tracking: false, cancellationToken);
        var cutOff = await InterestCutOff.ForAsync(db, schedule.TenantId, schedule.Currency, InterestCutOff.Today(time), cancellationToken);
        return InterestRateScheduleDto.From(schedule, cutOff);
    }
}
