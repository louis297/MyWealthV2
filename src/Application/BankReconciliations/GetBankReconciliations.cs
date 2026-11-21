using MyWealthV2.Application.ClientMoneyAccounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>R20: the current tenant's runs, newest first.</summary>
[Authorize(Policy = Policies.BankReconciliationsManage)]
public sealed record GetBankReconciliationsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? ClientMoneyAccountId = null,
    string? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? TenantId = null) : IRequest<PagedList<BankReconciliationListItemDto>>
{
    internal static IReadOnlyList<string> StatusNames(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? []
            : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public sealed class GetBankReconciliationsQueryValidator : AbstractValidator<GetBankReconciliationsQuery>
{
    private static readonly string[] Names = Enum.GetNames<BankReconciliationStatus>();

    public GetBankReconciliationsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetBankReconciliationsQuery.StatusNames(status)
                .All(name => Names.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Names)}, comma-separated.");
        RuleFor(query => query.To)
            .Must((query, to) => query.From is null || to is null || to >= query.From)
            .WithMessage("To must not be before From.");
    }
}

public sealed class GetBankReconciliationsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetBankReconciliationsQuery, PagedList<BankReconciliationListItemDto>>
{
    public async Task<PagedList<BankReconciliationListItemDto>> Handle(
        GetBankReconciliationsQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = BankReconciliationReader.CurrentTenantId(currentUser);
        var query = db.BankReconciliations.AsNoTracking().Where(row => row.TenantId == tenantId);

        if (request.ClientMoneyAccountId is Guid clientMoneyAccountId)
        {
            query = query.Where(row => db.ClientMoneyAccounts.Any(account =>
                account.Id == row.ClientMoneyAccountId && account.PublicId == clientMoneyAccountId));
        }

        var statuses = GetBankReconciliationsQuery.StatusNames(request.Status)
            .Select(name => Enum.Parse<BankReconciliationStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
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
        var runs = await query
            .OrderByDescending(row => row.RanAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<BankReconciliationListItemDto>
        {
            Items = await BankReconciliationReader.ToListItemsAsync(db, runs, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
