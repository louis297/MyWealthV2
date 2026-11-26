using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Valuations.Queries.GetStoredValuations;

/// <summary>Valuation-run R1, R17, R19, R21: the stored valuations of one Account, newest day first.</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetStoredValuationsQuery(
    Guid AccountId,
    int Page = 1,
    int PageSize = 20,
    string? From = null,
    string? To = null,
    bool? Complete = null) : IRequest<PagedList<StoredValuationSummaryDto>>;

/// <summary>R17: <c>from</c> and <c>to</c> are <c>yyyy-MM-dd</c> days, <c>from</c> not after <c>to</c>; at most 100 a page.</summary>
public class GetStoredValuationsQueryValidator : AbstractValidator<GetStoredValuationsQuery>
{
    public GetStoredValuationsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.From).Must(MarketDataDates.IsEmptyOrDate).WithMessage($"From {MarketDataDates.FormatMessage}");
        RuleFor(query => query.To).Must(MarketDataDates.IsEmptyOrDate).WithMessage($"To {MarketDataDates.FormatMessage}");
        RuleFor(query => query.From)
            .Must((query, from) => !MarketDataDates.FromIsAfterTo(from, query.To))
            .WithMessage("From must not be after To.");
    }
}

public class GetStoredValuationsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetStoredValuationsQuery, PagedList<StoredValuationSummaryDto>>
{
    public async Task<PagedList<StoredValuationSummaryDto>> Handle(
        GetStoredValuationsQuery request, CancellationToken cancellationToken)
    {
        var account = await StoredValuationReader.AccountAsync(db, currentUser, request.AccountId, cancellationToken);
        var query = db.StoredValuations.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.AccountId == account.Id && row.TenantId == account.TenantId);
        if (MarketDataDates.Parse(request.From) is { } from)
        {
            query = query.Where(row => row.Date >= from);
        }

        if (MarketDataDates.Parse(request.To) is { } to)
        {
            query = query.Where(row => row.Date <= to);
        }

        if (request.Complete is { } complete)
        {
            query = query.Where(row => row.Complete == complete);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(row => row.Date)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(row => new StoredValuationSummaryDto(
                row.Date, row.Currency, row.Cash, row.HoldingsValue, row.Total, row.Complete, row.UnvaluedCount))
            .ToListAsync(cancellationToken);
        return new PagedList<StoredValuationSummaryDto>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

/// <summary>Valuation-run R1, R18, R19, R21: the stored valuation of one Account and one day.</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetStoredValuationQuery(Guid AccountId, string? Date) : IRequest<StoredValuationDto>;

/// <summary>R18: <c>{date}</c> is a <c>yyyy-MM-dd</c> day.</summary>
public class GetStoredValuationQueryValidator : AbstractValidator<GetStoredValuationQuery>
{
    public GetStoredValuationQueryValidator()
    {
        RuleFor(query => query.Date)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Date is required.")
            .Must(MarketDataDates.IsEmptyOrDate).WithMessage($"Date {MarketDataDates.FormatMessage}");
    }
}

public class GetStoredValuationQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetStoredValuationQuery, StoredValuationDto>
{
    public async Task<StoredValuationDto> Handle(GetStoredValuationQuery request, CancellationToken cancellationToken)
    {
        var account = await StoredValuationReader.AccountAsync(db, currentUser, request.AccountId, cancellationToken);
        var day = MarketDataDates.Parse(request.Date)!.Value;
        var stored = await db.StoredValuations.IgnoreQueryFilters().AsNoTracking().Include(row => row.Lines)
                         .SingleOrDefaultAsync(row => row.AccountId == account.Id && row.TenantId == account.TenantId && row.Date == day,
                             cancellationToken)
                     ?? throw new NotFoundException("StoredValuation", $"{request.AccountId}/{request.Date}");
        var runId = await db.ValuationRuns.IgnoreQueryFilters().Where(run => run.Id == stored.ValuationRunId)
            .Select(run => run.PublicId).SingleAsync(cancellationToken);

        // R18: symbol and name are the Instrument's now; holdings sorted as account-valuation R21.
        var ids = stored.Lines.Select(line => line.InstrumentId).ToList();
        var instruments = await db.Instruments.IgnoreQueryFilters().AsNoTracking()
            .Where(instrument => ids.Contains(instrument.Id) && instrument.TenantId == account.TenantId)
            .Select(instrument => new { instrument.Id, instrument.PublicId, instrument.Symbol, instrument.Name })
            .ToDictionaryAsync(instrument => instrument.Id, cancellationToken);
        var holdings = stored.Lines
            .Select(line => (Line: line, Instrument: instruments[line.InstrumentId]))
            .OrderBy(row => row.Instrument.Symbol, StringComparer.Ordinal)
            .ThenBy(row => row.Instrument.PublicId)
            .Select(row => new AccountValuationHoldingDto(
                row.Instrument.PublicId,
                row.Instrument.Symbol,
                row.Instrument.Name,
                row.Line.Quantity,
                row.Line.LedgerQuantity,
                row.Line.Status.ToString(),
                row.Line.PriceAmount is { } amount
                    ? new AccountValuationPriceDto(amount, row.Line.PriceCurrency!, row.Line.PriceDate!.Value, row.Line.PriceSource!)
                    : null,
                row.Line.MarketValue,
                row.Line.FxRate is { } rate ? new AccountValuationFxDto(rate, row.Line.FxDate!.Value, row.Line.FxSource!) : null,
                row.Line.Value))
            .ToList();

        return new StoredValuationDto(
            account.PublicId,
            stored.Date,
            stored.Currency,
            stored.Cash,
            holdings,
            stored.HoldingsValue,
            stored.Total,
            stored.Complete,
            stored.UnvaluedCount,
            runId,
            stored.ValuedAt);
    }
}

internal static class StoredValuationReader
{
    /// <summary>R1: the Account found as <c>GET /accounts/{id}/valuation</c> finds it; any other Account is 404.</summary>
    public static async Task<Account> AccountAsync(
        IApplicationDbContext db, ICurrentUser currentUser, Guid accountId, CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var selfCustomerId = AccountScope.SelfCustomerId(currentUser);
        var accounts =
            from account in db.Accounts.AsNoTracking()
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where account.PublicId == accountId
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
                  && (selfCustomerId == null || customer.Id == selfCustomerId)
            select account;

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            var tenantId = currentUser.TenantId ?? throw new NotFoundException("Account", accountId);
            accounts = accounts.Where(account => account.TenantId == tenantId);
        }

        return await accounts.SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Account", accountId);
    }
}
