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

    /// <summary>Dividends R51: the dividend fields, each <c>null</c> on an entitlement of another kind.</summary>
    public decimal? EntitledQuantity { get; init; }

    public decimal? GrossAmount { get; init; }

    public decimal? ForeignTax { get; init; }

    public string? Currency { get; init; }

    public bool? Reinvests { get; init; }

    public decimal? ParticipatingQuantity { get; init; }

    public decimal? ReinvestedQuantity { get; init; }

    public decimal? NetAmount { get; init; }

    public string? NetCurrency { get; init; }
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

public class GetCorporateActionEntitlementsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCorporateActionEntitlementsQuery, PagedList<CorporateActionEntitlementDto>>
{
    public async Task<PagedList<CorporateActionEntitlementDto>> Handle(
        GetCorporateActionEntitlementsQuery request,
        CancellationToken cancellationToken)
    {
        var actionId = await CorporateActionReader.Scope(db, currentUser).AsNoTracking()
                           .Where(action => action.PublicId == request.Id)
                           .Select(action => (int?)action.Id)
                           .SingleOrDefaultAsync(cancellationToken)
                       ?? throw new Common.Exceptions.NotFoundException("CorporateAction", request.Id);

        // R36: a TenantAdmin sees every entitlement, an Adviser those of assigned Customers' accounts.
        var accounts = AccountScope.ManagedAccounts(db, currentUser);
        var query = db.CorporateActionEntitlements.AsNoTracking()
            .Where(entitlement => entitlement.CorporateActionId == actionId
                                  && accounts.Any(account => account.Id == entitlement.AccountId));
        var statuses = GetCorporateActionsQuery.Names(request.Status)
            .Select(name => Enum.Parse<EntitlementStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(entitlement => statuses.Contains(entitlement.Status));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await (
                from entitlement in query
                join account in db.Accounts.AsNoTracking() on entitlement.AccountId equals account.Id
                join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
                join transaction in db.Transactions.AsNoTracking() on entitlement.TransactionId equals transaction.Id into postings
                from transaction in postings.DefaultIfEmpty()
                orderby entitlement.AccountId
                select new
                {
                    entitlement,
                    AccountId = account.PublicId,
                    AccountName = account.Name,
                    CustomerId = customer.PublicId,
                    CustomerName = customer.Name,
                    TransactionId = transaction == null ? (Guid?)null : transaction.PublicId,
                    QuoteCurrency = db.CorporateActions.Where(action => action.Id == entitlement.CorporateActionId)
                        .Join(db.Instruments, action => action.InstrumentId, instrument => instrument.Id, (action, instrument) => instrument.QuoteCurrency)
                        .FirstOrDefault()
                })
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<CorporateActionEntitlementDto>
        {
            Items = rows.Select(row => new CorporateActionEntitlementDto
            {
                AccountId = row.AccountId,
                AccountName = row.AccountName,
                CustomerId = row.CustomerId,
                CustomerName = row.CustomerName,
                Status = row.entitlement.Status.ToString(),
                Reason = row.entitlement.Reason?.ToString(),
                QuantityBefore = row.entitlement.QuantityBefore,
                QuantityAfter = row.entitlement.QuantityAfter,
                TransactionId = row.TransactionId,
                PostedAt = row.entitlement.PostedAt,
                EntitledQuantity = row.entitlement.EntitledQuantity,
                GrossAmount = row.entitlement.GrossAmount,
                ForeignTax = row.entitlement.ForeignTax,
                Currency = row.entitlement.IsDividend ? row.QuoteCurrency?.Trim() : null,
                Reinvests = row.entitlement.IsDividend ? row.entitlement.Reinvests : null,
                ParticipatingQuantity = row.entitlement.ParticipatingQuantity,
                ReinvestedQuantity = row.entitlement.ReinvestedQuantity,
                NetAmount = row.entitlement.NetAmount,
                NetCurrency = row.entitlement.NetCurrency?.Trim()
            }).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
