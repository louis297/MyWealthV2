using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>An announcement item (docs/features/corporate-actions.md R14).</summary>
public sealed record CorporateActionAnnouncementDto(
    Guid Id,
    string Connection,
    string OutsideId,
    string PartyActionId,
    int Version,
    string PartyStatus,
    string Symbol,
    string Currency,
    string? Kind,
    int RatioNew,
    int RatioHeld,
    DateOnly ExDate,
    DateOnly? RecordDate,
    DateOnly? PaymentDate,
    DateTimeOffset ReceivedAt,
    string Status,
    string? SkipReason,
    DateTimeOffset? ProcessedAt);

/// <summary>Corporate-actions R14: the stored announcements, newest first. Platform data for a SystemAdmin.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public sealed record GetCorporateActionAnnouncementsQuery(int Page = 1, int PageSize = 20, string? Connection = null, string? Status = null)
    : IRequest<PagedList<CorporateActionAnnouncementDto>>;

public class GetCorporateActionAnnouncementsQueryValidator : AbstractValidator<GetCorporateActionAnnouncementsQuery>
{
    private static readonly string[] Statuses = Enum.GetNames<AnnouncementStatus>();

    public GetCorporateActionAnnouncementsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Status)
            .Must(status => GetCorporateActionsQuery.Names(status).All(name => Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Statuses)}, comma-separated.");
    }
}

public class GetCorporateActionAnnouncementsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetCorporateActionAnnouncementsQuery, PagedList<CorporateActionAnnouncementDto>>
{
    public async Task<PagedList<CorporateActionAnnouncementDto>> Handle(
        GetCorporateActionAnnouncementsQuery request,
        CancellationToken cancellationToken)
    {
        var query = db.CorporateActionAnnouncements.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Connection))
        {
            var connection = request.Connection.Trim();
            query = query.Where(row => row.Connection == connection);
        }

        var statuses = GetCorporateActionsQuery.Names(request.Status)
            .Select(name => Enum.Parse<AnnouncementStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.ReceivedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<CorporateActionAnnouncementDto>
        {
            Items = rows.Select(row => new CorporateActionAnnouncementDto(
                row.PublicId, row.Connection, row.OutsideId, row.PartyActionId, row.Version, row.PartyStatus.ToString(), row.Symbol,
                row.Currency.Trim(), row.Kind?.ToString(), row.RatioNew, row.RatioHeld, row.ExDate, row.RecordDate, row.PaymentDate,
                row.ReceivedAt, row.Status.ToString(), row.SkipReason?.ToString(), row.ProcessedAt)).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
