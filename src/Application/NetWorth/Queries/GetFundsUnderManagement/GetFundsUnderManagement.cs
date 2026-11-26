using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Application.Valuations;
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
    ICurrencyCatalog currencies,
    TimeProvider time) : IRequestHandler<GetFundsUnderManagementQuery, FundsUnderManagementDto>
{
    public async Task<FundsUnderManagementDto> Handle(GetFundsUnderManagementQuery request, CancellationToken cancellationToken)
    {
        var tenant = await TenantAsync(request, cancellationToken);
        var tenantId = tenant.Id;
        var reportingCurrency = tenant.ReportingCurrency.Trim();
        var decimalPlaces = currencies.TryGet(reportingCurrency)?.DecimalPlaces
                            ?? throw new InvalidOperationException($"Currency {reportingCurrency} is not in the catalog.");
        var (start, end) = FundsUnderManagementRange.Resolve(request.From, request.To, FundsUnderManagementRange.LastFinalDay(time));

        // R4: the Custodial accounts of the Customers in scope now, whatever their status.
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var selfCustomerId = AccountScope.SelfCustomerId(currentUser);
        var scope =
            from account in db.Accounts.IgnoreQueryFilters()
            join customer in db.Users.IgnoreQueryFilters() on account.CustomerId equals customer.Id
            where account.TenantId == tenantId
                  && account.CustodyMode == CustodyMode.Custodial
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
                  && (selfCustomerId == null || account.CustomerId == selfCustomerId)
            select account;

        // R6: the days each account is expected on.
        var accounts = await scope.AsNoTracking()
            .Select(account => new { account.Id, account.Created, account.Status })
            .ToListAsync(cancellationToken);
        var closedIds = accounts.Where(account => account.Status == AccountStatus.Closed).Select(account => account.Id).ToList();
        var closures = await ValuationDays.LastClosuresAsync(db, tenantId, closedIds, cancellationToken);
        var spans = accounts
            .Select(account => (
                First: ValuationDays.FirstDay(account.Created),
                Last: ValuationDays.LastDay(account.Status, closures.GetValueOrDefault(account.Id))))
            .ToList();

        // R5: per day and currency, the sum of the stored totals, with the number stored and incomplete.
        var sums = await (
            from row in db.StoredValuations.IgnoreQueryFilters().AsNoTracking()
            join account in scope on row.AccountId equals account.Id
            where row.TenantId == tenantId && row.Date >= start && row.Date <= end
            group row by new { row.Date, row.Currency }
            into grouped
            select new
            {
                grouped.Key.Date,
                grouped.Key.Currency,
                Total = grouped.Sum(row => row.Total),
                Stored = grouped.Count(),
                Incomplete = grouped.Count(row => !row.Complete)
            }).ToListAsync(cancellationToken);
        var byDay = sums.ToLookup(row => row.Date);

        var rates = new Dictionary<string, StoredRate>();
        var days = new List<FundsUnderManagementDayDto>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var rows = byDay[day].ToList();
            var expected = spans.Count(span => ValuationDays.IsExpected(day, span.First, span.Last));
            var stored = rows.Sum(row => row.Stored);
            var incomplete = rows.Sum(row => row.Incomplete);
            var missing = Math.Max(0, expected - stored);
            var totals = rows
                .GroupBy(row => row.Currency.Trim())
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Total));
            var folded = FxFold.Compute(totals, reportingCurrency, decimalPlaces, rates, day);
            days.Add(new FundsUnderManagementDayDto(
                day,
                folded.Lines.Select(FoldLineDto.From).ToList(),
                folded.Total,
                missing == 0 && incomplete == 0 && folded.Complete,
                expected,
                incomplete,
                missing));
        }

        return new FundsUnderManagementDto(reportingCurrency, start, end, days);
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
