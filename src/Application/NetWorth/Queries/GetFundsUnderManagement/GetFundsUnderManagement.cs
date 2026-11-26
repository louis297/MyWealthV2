using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Valuations;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.NetWorth.Queries.GetFundsUnderManagement;

/// <summary>Valuation-history R1, R3–R13: the funds under management of the caller's scope for every day of a range.</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetFundsUnderManagementQuery(Guid? TenantId = null, string? From = null, string? To = null)
    : IRequest<FundsUnderManagementDto>;

/// <summary>R3: the days of the series. <c>to</c> defaults to the last final day, <c>from</c> to 89 days before
/// <c>to</c>.</summary>
public static class FundsUnderManagementRange
{
    public const int DefaultDays = 90;
    public const int MaxDays = 366;

    public static DateOnly LastFinalDay(TimeProvider time) => ValuationRules.LastFinalDay(LedgerDay.Of(time.GetUtcNow()));

    public static (DateOnly From, DateOnly To) Resolve(string? from, string? to, DateOnly lastFinalDay)
    {
        var last = MarketDataDates.Parse(to) ?? lastFinalDay;
        return (MarketDataDates.Parse(from) ?? last.AddDays(-(DefaultDays - 1)), last);
    }
}

public class GetFundsUnderManagementQueryValidator : AbstractValidator<GetFundsUnderManagementQuery>
{
    public GetFundsUnderManagementQueryValidator(ICurrentUser currentUser, TimeProvider time)
    {
        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId).NotEmpty().WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId).Must(tenantId => tenantId is null).WithMessage("TenantId cannot be set.");
        });

        RuleFor(query => query.From).Must(MarketDataDates.IsEmptyOrDate).WithMessage($"From {MarketDataDates.FormatMessage}");
        RuleFor(query => query.To).Must(MarketDataDates.IsEmptyOrDate).WithMessage($"To {MarketDataDates.FormatMessage}");
        RuleFor(query => query.To)
            .Must(to => MarketDataDates.Parse(to) is not { } last || last <= FundsUnderManagementRange.LastFinalDay(time))
            .WithMessage("Funds under management can be read only up to the day before yesterday.");
        When(query => MarketDataDates.IsEmptyOrDate(query.From) && MarketDataDates.IsEmptyOrDate(query.To), () =>
        {
            RuleFor(query => query.From)
                .Must((query, _) => Range(query, time).From <= Range(query, time).To)
                .WithMessage("From must not be after To.");
            RuleFor(query => query.From)
                .Must((query, _) => Range(query, time) is var range
                                    && (range.From > range.To || range.To.DayNumber - range.From.DayNumber < FundsUnderManagementRange.MaxDays))
                .WithMessage($"A series covers at most {FundsUnderManagementRange.MaxDays} days.");
        });
    }

    private static (DateOnly From, DateOnly To) Range(GetFundsUnderManagementQuery query, TimeProvider time) =>
        FundsUnderManagementRange.Resolve(query.From, query.To, FundsUnderManagementRange.LastFinalDay(time));
}

public class GetFundsUnderManagementQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider time) : IRequestHandler<GetFundsUnderManagementQuery, FundsUnderManagementDto>
{
    public async Task<FundsUnderManagementDto> Handle(GetFundsUnderManagementQuery request, CancellationToken cancellationToken)
    {
        var tenant = await TenantAsync(request, cancellationToken);
        var (from, to) = FundsUnderManagementRange.Resolve(request.From, request.To, FundsUnderManagementRange.LastFinalDay(time));

        var days = new List<FundsUnderManagementDayDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            days.Add(new FundsUnderManagementDayDto(day, [], 0m, true, 0, 0, 0));
        }

        return new FundsUnderManagementDto(tenant.ReportingCurrency.Trim(), from, to, days);
    }

    private async Task<Domain.Entities.Tenant> TenantAsync(GetFundsUnderManagementQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            return await db.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.PublicId == request.TenantId, cancellationToken)
                   ?? throw new NotFoundException("Tenant", request.TenantId!);
        }

        var tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        return await db.Tenants.AsNoTracking().IgnoreQueryFilters().SingleAsync(row => row.Id == tenantId, cancellationToken);
    }
}
