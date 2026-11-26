using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Exceptions;
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
    IFxRate fxRates,
    TimeProvider time) : IRequestHandler<GetFundsUnderManagementQuery, FundsUnderManagementDto>
{
    public async Task<FundsUnderManagementDto> Handle(GetFundsUnderManagementQuery request, CancellationToken cancellationToken)
    {
        var tenant = await TenantAsync(request, cancellationToken);
        var tenantId = tenant.Id;
        var reportingCurrency = tenant.ReportingCurrency.Trim();
        var decimalPlaces = Places(reportingCurrency);
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

        // R10: the rows of each other currency into the reporting currency, read once per pair.
        var rateRows = new Dictionary<string, RateRows>();
        foreach (var currency in sums.Select(row => row.Currency.Trim()).Where(code => code != reportingCurrency).Distinct())
        {
            rateRows[currency] = await RateRowsAsync(currency, reportingCurrency, start, end, cancellationToken);
        }

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
            var rates = new Dictionary<string, StoredRate>();
            foreach (var currency in totals.Keys.Where(rateRows.ContainsKey))
            {
                if (rateRows[currency].On(day) is { } rate)
                {
                    rates[currency] = rate;
                }
            }

            var folded = FxFold.Compute(totals, reportingCurrency, decimalPlaces, rates, day);
            days.Add(new FundsUnderManagementDayDto(
                day,
                folded.Lines.Select(line => FoldLineDto.From(line, Places, reportingCurrency)).ToList(),
                decimal.Round(folded.Total, decimalPlaces, MidpointRounding.AwayFromZero),
                missing == 0 && incomplete == 0 && folded.Complete,
                expected,
                incomplete,
                missing));
        }

        return new FundsUnderManagementDto(reportingCurrency, start, end, days);
    }

    private int Places(string currency) =>
        currencies.TryGet(currency)?.DecimalPlaces
        ?? throw new InvalidOperationException($"Currency {currency} is not in the catalog.");

    /// <summary>
    /// The rows of the pair dated from the age limit before <paramref name="start"/> to <paramref name="end"/>, and the
    /// latest row before them, so that a day before the first listed row still finds a row past the age limit
    /// (<c>StaleRate</c>) rather than none (<c>NoRate</c>).
    /// </summary>
    private async Task<RateRows> RateRowsAsync(
        string currency, string reportingCurrency, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var windowStart = start.AddDays(-ValuationRules.MaxAgeDays);
        var listed = await fxRates.ListRatesAsync(
            currency, reportingCurrency, windowStart, end, 1, end.DayNumber - windowStart.DayNumber + 1, cancellationToken);
        StoredRate? before = null;
        try
        {
            var dayBefore = windowStart.AddDays(-1);
            var reading = await fxRates.GetRateDetailAsync(
                currency, reportingCurrency, new DateTimeOffset(dayBefore.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), cancellationToken);
            before = new StoredRate(reading.Rate, reading.AsOfDate, reading.Source.Trim());
        }
        catch (FxRateNotFoundException)
        {
            // R10: no row before the window; a day with no listed row is NoRate.
        }

        var rows = listed.Items
            .Select(row => new StoredRate(row.Rate, row.AsOfDate, row.Source.Trim()))
            .OrderBy(row => row.AsOfDate)
            .ToList();
        return new RateRows(rows, before);
    }

    /// <summary>The rows of one pair, oldest first, and the latest row before them.</summary>
    private sealed record RateRows(IReadOnlyList<StoredRate> Rows, StoredRate? Before)
    {
        public StoredRate? On(DateOnly day) => Rows.LastOrDefault(row => row.AsOfDate <= day) ?? Before;
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
