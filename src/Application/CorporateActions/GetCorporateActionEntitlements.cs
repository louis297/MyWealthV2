using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>An entitlement item (docs/features/corporate-actions.md R36, §8.1).</summary>
public sealed class CorporateActionEntitlementDto
{
    public required Guid AccountId { get; init; }

    public required string AccountName { get; init; }

    public required Guid CustomerId { get; init; }

    public required string CustomerName { get; init; }

    public required string Status { get; init; }

    public string? Reason { get; init; }

    public decimal? QuantityBefore { get; init; }

    public decimal? QuantityAfter { get; init; }

    public Guid? TransactionId { get; init; }

    public DateTimeOffset? PostedAt { get; init; }
}

/// <summary>Corporate-actions R36: the entitlements of one corporate action the caller can see, account order.</summary>
[Authorize(Policy = Policies.CorporateActionsRead)]
public sealed record GetCorporateActionEntitlementsQuery(Guid Id, int Page = 1, int PageSize = 20, string? Status = null)
    : IRequest<PagedList<CorporateActionEntitlementDto>>;

public class GetCorporateActionEntitlementsQueryValidator : AbstractValidator<GetCorporateActionEntitlementsQuery>
{
    private static readonly string[] Statuses = Enum.GetNames<EntitlementStatus>();

    public GetCorporateActionEntitlementsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Status)
            .Must(status => GetCorporateActionsQuery.Names(status).All(name => Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Statuses)}, comma-separated.");
    }
}

public class GetCorporateActionEntitlementsQueryHandler
    : IRequestHandler<GetCorporateActionEntitlementsQuery, PagedList<CorporateActionEntitlementDto>>
{
    public Task<PagedList<CorporateActionEntitlementDto>> Handle(
        GetCorporateActionEntitlementsQuery request,
        CancellationToken cancellationToken) => throw new NotImplementedException();
}
