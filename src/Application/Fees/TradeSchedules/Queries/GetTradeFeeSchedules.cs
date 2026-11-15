using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Portfolios;

namespace MyWealthV2.Application.Fees.TradeSchedules.Queries;

/// <summary>List trade fee schedules of one tenant, sorted by quote currency then effective date (trade-fees §8).</summary>
[Authorize(Policy = Policies.FeesRead)]
public record GetTradeFeeSchedulesQuery(
    int Page = 1,
    int PageSize = 20,
    string? QuoteCurrency = null,
    Guid? TenantId = null)
    : IRequest<PagedList<TradeFeeScheduleDto>>;

public class GetTradeFeeSchedulesQueryValidator : AbstractValidator<GetTradeFeeSchedulesQuery>
{
    public GetTradeFeeSchedulesQueryValidator(ICurrentUser currentUser)
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        When(_ => currentUser.Role == Domain.Enums.UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId).NotEmpty().WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != Domain.Enums.UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

public class GetTradeFeeSchedulesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<GetTradeFeeSchedulesQuery, PagedList<TradeFeeScheduleDto>>
{
    public async Task<PagedList<TradeFeeScheduleDto>> Handle(
        GetTradeFeeSchedulesQuery request,
        CancellationToken cancellationToken)
    {
        var tenant = await PortfolioTenant.ResolveAsync(db, currentUser, request.TenantId, cancellationToken);
        var query = db.TradeFeeSchedules.AsNoTracking().Where(schedule => schedule.TenantId == tenant.Id);
        if (!string.IsNullOrWhiteSpace(request.QuoteCurrency))
        {
            var code = request.QuoteCurrency.Trim().ToUpperInvariant();
            query = query.Where(schedule => schedule.QuoteCurrency == code);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var schedules = await query
            .Include(schedule => schedule.BandRows)
            .OrderBy(schedule => schedule.QuoteCurrency)
            .ThenBy(schedule => schedule.EffectiveFrom)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var cutOffs = await TradeFeeCutOff.ForAsync(
            db, tenant.Id, schedules.Select(schedule => schedule.QuoteCurrency).ToList(),
            TradeFeeCutOff.Today(time), cancellationToken);

        return new PagedList<TradeFeeScheduleDto>
        {
            Items = schedules.Select(schedule => TradeFeeScheduleDto.From(schedule, cutOffs[schedule.QuoteCurrency])).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
