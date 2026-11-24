using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.SafekeepingAccounts;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.CustodianReconciliations;

/// <summary>The safekeeping account of a run or an overview entry (in-kind-transfers R59).</summary>
public sealed record CustodianReconciliationAccountDto(
    Guid Id, string Currency, string CustodianName, string AccountName, string AccountNumber, bool IsActive);

public sealed record CustodianReconciliationFeedDto(DateTimeOffset? LastSucceededAt, DateTimeOffset? LastFailedAt, string? LastFailure);

public sealed record CustodianReconciliationInstrumentDto(Guid Id, string Symbol, string Name);

public sealed record CustodianReconciliationLineDto(
    CustodianReconciliationInstrumentDto? Instrument,
    string Symbol,
    decimal CustodianQuantity,
    decimal PlatformQuantity,
    decimal Difference,
    decimal NotProcessed,
    decimal UnsettledTrades,
    decimal Suspense,
    decimal UnbookedFills,
    decimal Unexplained,
    string Class);

/// <summary>R59: a run without its lines and feed status.</summary>
public class CustodianReconciliationListItemDto
{
    public required Guid Id { get; init; }

    public required CustodianReconciliationAccountDto SafekeepingAccount { get; init; }

    public required string Trigger { get; init; }

    public required DateTimeOffset RanAt { get; init; }

    public required PersonRefDto? RanBy { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset? PositionsAt { get; init; }

    public required int BreakCount { get; init; }

    public required int TimingCount { get; init; }

    public required int AgreedCount { get; init; }

    public required int TransfersInFlight { get; init; }

    public required int FillsNotDelivered { get; init; }
}

/// <summary>R59, §8.1: a run with every line.</summary>
public sealed class CustodianReconciliationDto : CustodianReconciliationListItemDto
{
    public required CustodianReconciliationFeedDto Feed { get; init; }

    public required IReadOnlyList<CustodianReconciliationLineDto> Lines { get; init; }
}

public sealed record CustodianReconciliationOverviewDto(
    CustodianReconciliationAccountDto SafekeepingAccount,
    CustodianFeedDto? CustodianFeed,
    CustodianReconciliationListItemDto? Latest);

internal static class CustodianReconciliationReader
{
    public static int CurrentTenantId(ICurrentUser currentUser) => currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");

    public static CustodianReconciliationAccountDto Account(SafekeepingAccount row) =>
        new(row.PublicId, row.Currency, row.CustodianName, row.AccountName, row.AccountNumber, row.IsActive);

    public static async Task<List<CustodianReconciliationListItemDto>> ToListItemsAsync(
        IApplicationDbContext db, IReadOnlyList<CustodianReconciliation> runs, CancellationToken cancellationToken)
    {
        var (accounts, people) = await LookupsAsync(db, runs, cancellationToken);
        return runs.Select(run => (CustodianReconciliationListItemDto)Fill(new CustodianReconciliationListItemDto
        {
            Id = run.PublicId,
            SafekeepingAccount = accounts[run.SafekeepingAccountId],
            Trigger = run.Trigger.ToString(),
            RanAt = run.RanAt,
            RanBy = run.RanByUserId is int by ? people[by] : null,
            Status = run.Status.ToString(),
            PositionsAt = run.PositionsAt,
            BreakCount = run.BreakCount,
            TimingCount = run.TimingCount,
            AgreedCount = run.AgreedCount,
            TransfersInFlight = run.TransfersInFlight,
            FillsNotDelivered = run.FillsNotDelivered
        })).ToList();
    }

    public static async Task<CustodianReconciliationDto> ToDtoAsync(
        IApplicationDbContext db, CustodianReconciliation run, CancellationToken cancellationToken)
    {
        var (accounts, people) = await LookupsAsync(db, [run], cancellationToken);
        var instrumentIds = run.Lines.Where(line => line.InstrumentId is not null).Select(line => line.InstrumentId!.Value).ToList();
        var instruments = await db.Instruments.AsNoTracking().Where(row => instrumentIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new CustodianReconciliationInstrumentDto(row.PublicId, row.Symbol, row.Name), cancellationToken);
        return new CustodianReconciliationDto
        {
            Id = run.PublicId,
            SafekeepingAccount = accounts[run.SafekeepingAccountId],
            Trigger = run.Trigger.ToString(),
            RanAt = run.RanAt,
            RanBy = run.RanByUserId is int by ? people[by] : null,
            Status = run.Status.ToString(),
            PositionsAt = run.PositionsAt,
            BreakCount = run.BreakCount,
            TimingCount = run.TimingCount,
            AgreedCount = run.AgreedCount,
            TransfersInFlight = run.TransfersInFlight,
            FillsNotDelivered = run.FillsNotDelivered,
            Feed = new CustodianReconciliationFeedDto(run.FeedLastSucceededAt, run.FeedLastFailedAt, run.FeedLastFailure?.ToString()),
            Lines = run.Lines.OrderBy(line => line.Position).Select(line => new CustodianReconciliationLineDto(
                line.InstrumentId is int instrument ? instruments.GetValueOrDefault(instrument) : null,
                line.Symbol, line.CustodianQuantity, line.PlatformQuantity, line.Difference, line.NotProcessed, line.UnsettledTrades,
                line.Suspense, line.UnbookedFills, line.Unexplained, line.Class.ToString())).ToList()
        };
    }

    private static CustodianReconciliationListItemDto Fill(CustodianReconciliationListItemDto item) => item;

    private static async Task<(Dictionary<int, CustodianReconciliationAccountDto> Accounts, Dictionary<int, PersonRefDto> People)> LookupsAsync(
        IApplicationDbContext db, IReadOnlyList<CustodianReconciliation> runs, CancellationToken cancellationToken)
    {
        var accountIds = runs.Select(run => run.SafekeepingAccountId).Distinct().ToList();
        var userIds = runs.Where(run => run.RanByUserId is not null).Select(run => run.RanByUserId!.Value).Distinct().ToList();
        var accounts = (await db.SafekeepingAccounts.AsNoTracking().Where(row => accountIds.Contains(row.Id)).ToListAsync(cancellationToken))
            .ToDictionary(row => row.Id, Account);
        var people = await db.Users.AsNoTracking().Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new PersonRefDto(row.PublicId, row.Name), cancellationToken);
        return (accounts, people);
    }
}

/// <summary>R58: "Run now" for one active safekeeping account of the current Tenant.</summary>
[Authorize(Policy = Policies.CustodianReconciliationsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RunCustodianReconciliationCommand : IRequest<Guid>
{
    public Guid? SafekeepingAccountId { get; init; }

    /// <summary>No caller names a tenant (R1).</summary>
    public Guid? TenantId { get; init; }
}

public sealed class RunCustodianReconciliationCommandValidator : AbstractValidator<RunCustodianReconciliationCommand>
{
    public RunCustodianReconciliationCommandValidator()
    {
        RuleFor(command => command.SafekeepingAccountId).NotEmpty();
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public sealed class RunCustodianReconciliationCommandHandler(
    IApplicationDbContext db, ICurrentUser currentUser, IReconcileSafekeepingAccount reconcile)
    : IRequestHandler<RunCustodianReconciliationCommand, Guid>
{
    public async Task<Guid> Handle(RunCustodianReconciliationCommand request, CancellationToken cancellationToken)
    {
        var tenantId = CustodianReconciliationReader.CurrentTenantId(currentUser);
        var account = await db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
                          .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.PublicId == request.SafekeepingAccountId, cancellationToken)
                      ?? throw new NotFoundException("Safekeeping account", request.SafekeepingAccountId!.Value);
        if (!account.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(RunCustodianReconciliationCommand.SafekeepingAccountId), "Only an active safekeeping account can be reconciled.")
            ]);
        }

        var person = currentUser.Person ?? throw new NotFoundException("User", "current");
        return await reconcile.RunAsync(account.Id, ReconciliationTrigger.Manual, person.Id, cancellationToken);
    }
}

/// <summary>R59: one entry per safekeeping account of the Tenant that is active or has a run.</summary>
[Authorize(Policy = Policies.CustodianReconciliationsManage)]
public sealed record GetCustodianReconciliationOverviewQuery(Guid? TenantId = null) : IRequest<PagedList<CustodianReconciliationOverviewDto>>;

public sealed class GetCustodianReconciliationOverviewQueryValidator : AbstractValidator<GetCustodianReconciliationOverviewQuery>
{
    public GetCustodianReconciliationOverviewQueryValidator() =>
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
}

public sealed class GetCustodianReconciliationOverviewQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCustodianReconciliationOverviewQuery, PagedList<CustodianReconciliationOverviewDto>>
{
    public async Task<PagedList<CustodianReconciliationOverviewDto>> Handle(
        GetCustodianReconciliationOverviewQuery request, CancellationToken cancellationToken)
    {
        var tenantId = CustodianReconciliationReader.CurrentTenantId(currentUser);
        var accounts = await db.SafekeepingAccounts.AsNoTracking()
            .Where(row => row.TenantId == tenantId
                          && (row.IsActive || db.CustodianReconciliations.Any(run => run.TenantId == tenantId && run.SafekeepingAccountId == row.Id)))
            .OrderBy(row => row.Currency).ThenByDescending(row => row.IsActive).ThenByDescending(row => row.Created).ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken);
        var ids = accounts.Select(row => row.Id).ToList();
        var cursors = await db.CustodianFeedCursors.AsNoTracking().Where(row => ids.Contains(row.SafekeepingAccountId))
            .ToDictionaryAsync(row => row.SafekeepingAccountId, cancellationToken);
        var latestIds = await db.CustodianReconciliations.AsNoTracking()
            .Where(row => row.TenantId == tenantId && ids.Contains(row.SafekeepingAccountId))
            .GroupBy(row => row.SafekeepingAccountId)
            .Select(group => group.OrderByDescending(row => row.RanAt).ThenByDescending(row => row.Id).Select(row => row.Id).First())
            .ToListAsync(cancellationToken);
        var latestRuns = await db.CustodianReconciliations.AsNoTracking().Where(row => latestIds.Contains(row.Id)).ToListAsync(cancellationToken);
        var latest = (await CustodianReconciliationReader.ToListItemsAsync(db, latestRuns, cancellationToken))
            .Zip(latestRuns).ToDictionary(pair => pair.Second.SafekeepingAccountId, pair => pair.First);
        var items = accounts.Select(account => new CustodianReconciliationOverviewDto(
            CustodianReconciliationReader.Account(account),
            CustodianFeedDto.From(cursors.GetValueOrDefault(account.Id)),
            latest.GetValueOrDefault(account.Id))).ToList();
        return new PagedList<CustodianReconciliationOverviewDto> { Items = items, Page = 1, PageSize = Math.Max(items.Count, 1), TotalCount = items.Count };
    }
}

/// <summary>R59: the current Tenant's runs, newest first.</summary>
[Authorize(Policy = Policies.CustodianReconciliationsManage)]
public sealed record GetCustodianReconciliationsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? SafekeepingAccountId = null,
    string? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? TenantId = null) : IRequest<PagedList<CustodianReconciliationListItemDto>>
{
    internal static IReadOnlyList<string> StatusNames(string? status) =>
        string.IsNullOrWhiteSpace(status) ? [] : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public sealed class GetCustodianReconciliationsQueryValidator : AbstractValidator<GetCustodianReconciliationsQuery>
{
    private static readonly string[] Names = Enum.GetNames<CustodianReconciliationStatus>();

    public GetCustodianReconciliationsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetCustodianReconciliationsQuery.StatusNames(status).All(name => Names.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Names)}, comma-separated.");
        RuleFor(query => query.To)
            .Must((query, to) => query.From is null || to is null || to >= query.From)
            .WithMessage("To must not be before From.");
    }
}

public sealed class GetCustodianReconciliationsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCustodianReconciliationsQuery, PagedList<CustodianReconciliationListItemDto>>
{
    public async Task<PagedList<CustodianReconciliationListItemDto>> Handle(GetCustodianReconciliationsQuery request, CancellationToken cancellationToken)
    {
        var tenantId = CustodianReconciliationReader.CurrentTenantId(currentUser);
        var query = db.CustodianReconciliations.AsNoTracking().Where(row => row.TenantId == tenantId);
        if (request.SafekeepingAccountId is Guid safekeepingId)
        {
            query = query.Where(row => db.SafekeepingAccounts.Any(account => account.Id == row.SafekeepingAccountId && account.PublicId == safekeepingId));
        }

        var statuses = GetCustodianReconciliationsQuery.StatusNames(request.Status)
            .Select(name => Enum.Parse<CustodianReconciliationStatus>(name, ignoreCase: true)).Distinct().ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        // UTC days on ranAt: from <= ranAt < to + 1 day.
        if (request.From is DateOnly from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(row => row.RanAt >= start);
        }

        if (request.To is DateOnly to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(row => row.RanAt < end);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var runs = await query.OrderByDescending(row => row.RanAt).ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        return new PagedList<CustodianReconciliationListItemDto>
        {
            Items = await CustodianReconciliationReader.ToListItemsAsync(db, runs, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

/// <summary>R59: one run of the current Tenant with every line.</summary>
[Authorize(Policy = Policies.CustodianReconciliationsManage)]
public sealed record GetCustodianReconciliationByIdQuery(Guid Id) : IRequest<CustodianReconciliationDto>;

public sealed class GetCustodianReconciliationByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCustodianReconciliationByIdQuery, CustodianReconciliationDto>
{
    public async Task<CustodianReconciliationDto> Handle(GetCustodianReconciliationByIdQuery request, CancellationToken cancellationToken)
    {
        var tenantId = CustodianReconciliationReader.CurrentTenantId(currentUser);
        var run = await db.CustodianReconciliations.AsNoTracking().Include(row => row.Lines)
                      .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.PublicId == request.Id, cancellationToken)
                  ?? throw new NotFoundException("Custodian reconciliation", request.Id);
        return await CustodianReconciliationReader.ToDtoAsync(db, run, cancellationToken);
    }
}
