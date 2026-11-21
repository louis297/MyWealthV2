using MyWealthV2.Application.ClientMoneyAccounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>R19: one entry per client money account of the tenant that is active or has a run.</summary>
[Authorize(Policy = Policies.BankReconciliationsManage)]
public sealed record GetBankReconciliationOverviewQuery(Guid? TenantId = null) : IRequest<PagedList<BankReconciliationOverviewDto>>;

public sealed class GetBankReconciliationOverviewQueryValidator : AbstractValidator<GetBankReconciliationOverviewQuery>
{
    public GetBankReconciliationOverviewQueryValidator()
    {
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public sealed class GetBankReconciliationOverviewQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetBankReconciliationOverviewQuery, PagedList<BankReconciliationOverviewDto>>
{
    public async Task<PagedList<BankReconciliationOverviewDto>> Handle(
        GetBankReconciliationOverviewQuery request,
        CancellationToken cancellationToken)
    {
        var tenantId = BankReconciliationReader.CurrentTenantId(currentUser);
        var accounts = await db.ClientMoneyAccounts.AsNoTracking()
            .Where(row => row.TenantId == tenantId
                          && (row.IsActive || db.BankReconciliations.Any(run => run.TenantId == tenantId && run.ClientMoneyAccountId == row.Id)))
            .OrderBy(row => row.Currency)
            .ThenByDescending(row => row.IsActive)
            .ThenByDescending(row => row.Created)
            .ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken);
        var ids = accounts.Select(row => row.Id).ToList();
        var cursors = await db.BankFeedCursors.AsNoTracking()
            .Where(row => ids.Contains(row.ClientMoneyAccountId))
            .ToDictionaryAsync(row => row.ClientMoneyAccountId, cancellationToken);
        var latestIds = await db.BankReconciliations.AsNoTracking()
            .Where(row => row.TenantId == tenantId && ids.Contains(row.ClientMoneyAccountId))
            .GroupBy(row => row.ClientMoneyAccountId)
            .Select(group => group.OrderByDescending(row => row.RanAt).ThenByDescending(row => row.Id).Select(row => row.Id).First())
            .ToListAsync(cancellationToken);
        var latestRuns = await db.BankReconciliations.AsNoTracking()
            .Where(row => latestIds.Contains(row.Id))
            .ToListAsync(cancellationToken);
        var latest = (await BankReconciliationReader.ToListItemsAsync(db, latestRuns, cancellationToken))
            .Zip(latestRuns)
            .ToDictionary(pair => pair.Second.ClientMoneyAccountId, pair => pair.First);

        var items = accounts.Select(account => new BankReconciliationOverviewDto(
                BankReconciliationReader.Account(account),
                cursors.GetValueOrDefault(account.Id) is { } cursor
                    ? new ClientMoneyAccountFeedDto(cursor.LastSucceededAt, cursor.LastFailedAt, cursor.LastFailure?.ToString())
                    : null,
                latest.GetValueOrDefault(account.Id)))
            .ToList();
        return new PagedList<BankReconciliationOverviewDto>
        {
            Items = items,
            Page = 1,
            PageSize = Math.Max(items.Count, 1),
            TotalCount = items.Count
        };
    }
}
