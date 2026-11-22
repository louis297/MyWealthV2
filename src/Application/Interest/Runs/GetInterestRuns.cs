using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Interest.Runs;

/// <summary>Interest-run R37: a Tenant's runs, newest start first. A TenantAdmin reads the current Tenant's; a
/// SystemAdmin names one.</summary>
[Authorize(Policy = Policies.InterestRunsRead)]
public record GetInterestRunsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? TenantId = null,
    string? Trigger = null,
    DateOnly? From = null,
    DateOnly? To = null) : IRequest<PagedList<InterestRunDto>>;

public class GetInterestRunsQueryValidator : AbstractValidator<GetInterestRunsQuery>
{
    public GetInterestRunsQueryValidator(ICurrentUser currentUser)
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId).NotEmpty().WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId).Must(tenantId => tenantId is null).WithMessage("TenantId cannot be set; the tenant is the caller's.");
        });
        RuleFor(query => query.Trigger)
            .Must(trigger => InterestRunTriggers.TryParse(trigger, out _))
            .WithMessage("Trigger must be Scheduled, Manual or AccountClosure, comma-separated.");
        RuleFor(query => query.To)
            .Must((query, to) => to is null || query.From is null || to >= query.From)
            .WithMessage("To must not be before From.");
    }
}

public class GetInterestRunsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetInterestRunsQuery, PagedList<InterestRunDto>>
{
    public async Task<PagedList<InterestRunDto>> Handle(GetInterestRunsQuery request, CancellationToken cancellationToken)
    {
        var tenantId = await InterestRunTriggers.TenantIdAsync(db, currentUser, request.TenantId, cancellationToken);
        InterestRunTriggers.TryParse(request.Trigger, out var triggers);
        var query = db.InterestRuns.IgnoreQueryFilters().AsNoTracking().Where(run => run.TenantId == tenantId);
        if (triggers.Count > 0)
        {
            query = query.Where(run => triggers.Contains(run.Trigger));
        }

        if (request.From is { } from)
        {
            query = query.Where(run => run.AsOfDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(run => run.AsOfDate <= to);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var runs = await query.OrderByDescending(run => run.StartedAt).ThenByDescending(run => run.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        return new PagedList<InterestRunDto>
        {
            Items = await InterestRunReader.ItemsAsync(db, runs, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

/// <summary>Interest-run R37: one run of the current Tenant (TenantAdmin) or of any Tenant (SystemAdmin).</summary>
[Authorize(Policy = Policies.InterestRunsRead)]
public record GetInterestRunByIdQuery(Guid Id) : IRequest<InterestRunDto>;

public class GetInterestRunByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetInterestRunByIdQuery, InterestRunDto>
{
    public async Task<InterestRunDto> Handle(GetInterestRunByIdQuery request, CancellationToken cancellationToken)
    {
        var query = db.InterestRuns.IgnoreQueryFilters().AsNoTracking().Where(run => run.PublicId == request.Id);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(run => run.TenantId == currentUser.TenantId);
        }

        var run = await query.SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("InterestRun", request.Id);
        return (await InterestRunReader.ItemsAsync(db, [run], cancellationToken)).Single();
    }
}

internal static class InterestRunTriggers
{
    public static bool TryParse(string? value, out List<InterestRunTrigger> triggers)
    {
        triggers = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<InterestRunTrigger>(part, ignoreCase: false, out var trigger) || !Enum.IsDefined(trigger)
                || int.TryParse(part, out _))
            {
                return false;
            }

            triggers.Add(trigger);
        }

        return true;
    }

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
}
