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

public class GetCorporateActionAnnouncementsQueryHandler
    : IRequestHandler<GetCorporateActionAnnouncementsQuery, PagedList<CorporateActionAnnouncementDto>>
{
    public Task<PagedList<CorporateActionAnnouncementDto>> Handle(
        GetCorporateActionAnnouncementsQuery request,
        CancellationToken cancellationToken) => throw new NotImplementedException();
}
