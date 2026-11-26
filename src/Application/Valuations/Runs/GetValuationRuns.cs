using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>Valuation-run R22: a Tenant's runs, newest start first. A TenantAdmin reads the current Tenant's; a
/// SystemAdmin names one.</summary>
[Authorize(Policy = Policies.ValuationRunsRead)]
public record GetValuationRunsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? TenantId = null,
    string? Trigger = null,
    string? From = null,
    string? To = null) : IRequest<PagedList<ValuationRunDto>>;

public class GetValuationRunsQueryValidator : AbstractValidator<GetValuationRunsQuery>
{
    public GetValuationRunsQueryValidator(ICurrentUser currentUser)
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId).NotEmpty().WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId).Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set; the tenant is the caller's.");
        });
        RuleFor(query => query.Trigger)
            .Must(trigger => ValuationRunReader.TryParseTriggers(trigger, out _))
            .WithMessage("Trigger must be Scheduled or Manual, comma-separated.");
        RuleFor(query => query.From).Must(MarketDataDates.IsEmptyOrDate).WithMessage($"From {MarketDataDates.FormatMessage}");
        RuleFor(query => query.To).Must(MarketDataDates.IsEmptyOrDate).WithMessage($"To {MarketDataDates.FormatMessage}");
        RuleFor(query => query.From)
            .Must((query, from) => !MarketDataDates.FromIsAfterTo(from, query.To))
            .WithMessage("From must not be after To.");
    }
}

public class GetValuationRunsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetValuationRunsQuery, PagedList<ValuationRunDto>>
{
    public async Task<PagedList<ValuationRunDto>> Handle(GetValuationRunsQuery request, CancellationToken cancellationToken)
    {
        var tenantId = await ValuationRunReader.TenantIdAsync(db, currentUser, request.TenantId, cancellationToken);
        ValuationRunReader.TryParseTriggers(request.Trigger, out var triggers);
        var query = db.ValuationRuns.IgnoreQueryFilters().AsNoTracking().Where(run => run.TenantId == tenantId);
        if (triggers.Count > 0)
        {
            query = query.Where(run => triggers.Contains(run.Trigger));
        }

        if (MarketDataDates.Parse(request.From) is { } from)
        {
            query = query.Where(run => run.ThroughDate >= from);
        }

        if (MarketDataDates.Parse(request.To) is { } to)
        {
            query = query.Where(run => run.ThroughDate <= to);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var runs = await query.OrderByDescending(run => run.StartedAt).ThenByDescending(run => run.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        return new PagedList<ValuationRunDto>
        {
            Items = await ValuationRunReader.ItemsAsync(db, runs, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

/// <summary>Valuation-run R22, R23: one run of the current Tenant (TenantAdmin) or of any Tenant (SystemAdmin).</summary>
[Authorize(Policy = Policies.ValuationRunsRead)]
public record GetValuationRunByIdQuery(Guid Id) : IRequest<ValuationRunDto>;

public class GetValuationRunByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetValuationRunByIdQuery, ValuationRunDto>
{
    public async Task<ValuationRunDto> Handle(GetValuationRunByIdQuery request, CancellationToken cancellationToken)
    {
        var query = db.ValuationRuns.IgnoreQueryFilters().AsNoTracking().Where(run => run.PublicId == request.Id);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(run => run.TenantId == currentUser.TenantId);
        }

        var run = await query.SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("ValuationRun", request.Id);
        return (await ValuationRunReader.ItemsAsync(db, [run], cancellationToken)).Single();
    }
}

internal static class ValuationRunReader
{
    /// <summary>R22: one or more trigger names, comma-separated; a number or an unknown name is refused.</summary>
    public static bool TryParseTriggers(string? value, out List<ValuationRunTrigger> triggers)
    {
        triggers = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, out _)
                || !Enum.TryParse<ValuationRunTrigger>(part, ignoreCase: false, out var trigger)
                || !Enum.IsDefined(trigger))
            {
                return false;
            }

            triggers.Add(trigger);
        }

        return true;
    }

    /// <summary>R22: the caller's Tenant, or for a SystemAdmin the one named (unknown → 404).</summary>
    public static async Task<int> TenantIdAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        Guid? tenantPublicId,
        CancellationToken cancellationToken)
    {
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            return currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        }

        return await db.Tenants.IgnoreQueryFilters().Where(tenant => tenant.PublicId == tenantPublicId)
                   .Select(tenant => (int?)tenant.Id).SingleOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException("Tenant", tenantPublicId!);
    }

    public static async Task<IReadOnlyList<ValuationRunDto>> ItemsAsync(
        IApplicationDbContext db,
        IReadOnlyList<ValuationRun> runs,
        CancellationToken cancellationToken)
    {
        var personIds = runs.Where(run => run.RanByUserId is not null).Select(run => run.RanByUserId!.Value).Distinct().ToList();
        var people = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(user => personIds.Contains(user.Id))
            .Select(user => new { user.Id, user.PublicId, user.Name })
            .ToDictionaryAsync(user => user.Id, cancellationToken);
        return runs.Select(run => new ValuationRunDto(
                run.PublicId,
                run.ThroughDate,
                run.Trigger.ToString(),
                run.RanByUserId is { } id && people.TryGetValue(id, out var person)
                    ? new ValuationRunPersonDto(person.PublicId, person.Name)
                    : null,
                run.StartedAt,
                run.CompletedAt,
                run.AccountsProcessed,
                run.AccountsFailed,
                run.DaysValued,
                run.DaysIncomplete))
            .ToList();
    }
}
