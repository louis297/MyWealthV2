using MyWealthV2.Application.ClientMoneyAccounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>Reads the runs of the current tenant and maps them to the list item and the item (R19–R21, §8.1).</summary>
internal static class BankReconciliationReader
{
    public static int CurrentTenantId(ICurrentUser currentUser) =>
        currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");

    /// <summary>The client money account of the current tenant; another tenant's or a missing one is 404.</summary>
    public static async Task<ClientMoneyAccount> FindAccountAsync(
        IApplicationDbContext db,
        int tenantId,
        Guid id,
        CancellationToken cancellationToken) =>
        await db.ClientMoneyAccounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.PublicId == id && row.TenantId == tenantId, cancellationToken)
        ?? throw new NotFoundException("Client money account", id);

    public static BankReconciliationAccountDto Account(ClientMoneyAccount account) => new(
        account.PublicId,
        account.Currency.Trim(),
        account.BankName,
        account.AccountName,
        account.AccountNumber,
        account.IsActive);

    public static async Task<List<BankReconciliationListItemDto>> ToListItemsAsync(
        IApplicationDbContext db,
        IReadOnlyList<BankReconciliation> runs,
        CancellationToken cancellationToken)
    {
        var (accounts, people) = await LookupsAsync(db, runs, cancellationToken);
        return runs.Select(run => (BankReconciliationListItemDto)ListItem(run, accounts, people)).ToList();
    }

    public static async Task<BankReconciliationDto> ToDtoAsync(
        IApplicationDbContext db,
        BankReconciliation run,
        CancellationToken cancellationToken)
    {
        var (accounts, people) = await LookupsAsync(db, [run], cancellationToken);
        var items = await db.BankReconciliationItems.AsNoTracking()
            .Where(row => row.TenantId == run.TenantId && row.ReconciliationId == run.Id)
            .OrderBy(row => row.Position)
            .ToListAsync(cancellationToken);
        var lineIds = items.Where(item => item.StatementLineId is not null).Select(item => item.StatementLineId!.Value).ToList();
        var lines = await db.BankStatementLines.AsNoTracking()
            .Where(row => row.TenantId == run.TenantId && lineIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);
        var otherIds = items.Where(item => item.OtherClientMoneyAccountId is not null)
            .Select(item => item.OtherClientMoneyAccountId!.Value).ToList();
        var others = await db.ClientMoneyAccounts.AsNoTracking()
            .Where(row => row.TenantId == run.TenantId && otherIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.PublicId, cancellationToken);

        var item = ListItem(run, accounts, people);
        return new BankReconciliationDto
        {
            Id = item.Id,
            ClientMoneyAccount = item.ClientMoneyAccount,
            Trigger = item.Trigger,
            RanAt = item.RanAt,
            RanBy = item.RanBy,
            Status = item.Status,
            BankBalance = item.BankBalance,
            BalanceAt = item.BalanceAt,
            OpeningBalance = item.OpeningBalance,
            StatementLinesTotal = item.StatementLinesTotal,
            CustodialCash = item.CustodialCash,
            Difference = item.Difference,
            Totals = item.Totals,
            ReservedCash = item.ReservedCash,
            PaymentsInFlight = new BankReconciliationPaymentsDto(run.PaymentsInFlightCount, run.PaymentsInFlightAmount),
            OpenSettlements = new BankReconciliationPaymentsDto(run.OpenSettlementsCount, run.OpenSettlementsAmount),
            Feed = new ClientMoneyAccountFeedDto(run.FeedLastSucceededAt, run.FeedLastFailedAt, run.FeedLastFailure?.ToString()),
            Items = items.Select(row => new BankReconciliationItemDto(
                row.Kind.ToString(),
                row.Class.ToString(),
                row.Amount,
                row.Count,
                row.StatementLineId is int lineId
                    ? new BankReconciliationLineDto(
                        lines[lineId],
                        row.Direction!.Value.ToString(),
                        row.ValueDate!.Value.ToString("yyyy-MM-dd"),
                        row.LineStatus!.Value.ToString(),
                        row.UnmatchedReason?.ToString(),
                        row.Note)
                    : null,
                row.OtherClientMoneyAccountId is int other ? others[other] : null)).ToList()
        };
    }

    private static BankReconciliationListItemDto ListItem(
        BankReconciliation run,
        IReadOnlyDictionary<int, ClientMoneyAccount> accounts,
        IReadOnlyDictionary<int, BankReconciliationPersonDto> people) => new()
    {
        Id = run.PublicId,
        ClientMoneyAccount = Account(accounts[run.ClientMoneyAccountId]),
        Trigger = run.Trigger.ToString(),
        RanAt = run.RanAt,
        RanBy = run.RanByUserId is int userId ? people.GetValueOrDefault(userId) : null,
        Status = run.Status.ToString(),
        BankBalance = run.BankBalance,
        BalanceAt = run.BalanceAt,
        OpeningBalance = run.OpeningBalance,
        StatementLinesTotal = run.StatementLinesTotal,
        CustodialCash = run.CustodialCash,
        Difference = run.Difference,
        Totals = new BankReconciliationTotalsDto(
            new BankReconciliationTotalDto(run.BreakCount, run.BreakTotal),
            new BankReconciliationTotalDto(run.TimingCount, run.TimingTotal),
            new BankReconciliationTotalDto(run.KnownCount, run.KnownTotal)),
        ReservedCash = run.ReservedCash
    };

    private static async Task<(Dictionary<int, ClientMoneyAccount> Accounts, Dictionary<int, BankReconciliationPersonDto> People)>
        LookupsAsync(IApplicationDbContext db, IReadOnlyList<BankReconciliation> runs, CancellationToken cancellationToken)
    {
        var accountIds = runs.Select(run => run.ClientMoneyAccountId).Distinct().ToList();
        var userIds = runs.Where(run => run.RanByUserId is not null).Select(run => run.RanByUserId!.Value).Distinct().ToList();
        var accounts = await db.ClientMoneyAccounts.AsNoTracking()
            .Where(row => accountIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var people = await db.Users.AsNoTracking()
            .Where(row => userIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => new BankReconciliationPersonDto(row.PublicId, row.Name), cancellationToken);
        return (accounts, people);
    }
}
