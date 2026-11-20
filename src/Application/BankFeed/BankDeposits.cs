using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// Posts the deposit of a statement line and reverses it (deposit-matching R32, R33). The caller runs it inside a
/// database transaction, after it has locked the Account row and checked the Account and the Customer.
/// </summary>
internal static class BankDeposits
{
    /// <summary>True when the line's amount has no more decimal places than its currency (R17, R26).</summary>
    public static bool AmountFits(ICurrencyCatalog catalog, BankStatementLine line) =>
        catalog.TryGet(line.Currency)?.DecimalPlaces is { } places
        && FxConversionPricing.FitsDecimalPlaces(line.Amount, places);

    /// <summary>Adds the deposit of <paramref name="line"/> on <paramref name="account"/> and saves it, so its Id is
    /// known. <paramref name="createdBy"/> is the system name for a match by reference (R32).</summary>
    public static async Task<Transaction> PostAsync(
        IApplicationDbContext db,
        BankStatementLine line,
        Account account,
        DateTimeOffset now,
        string? createdBy,
        CancellationToken cancellationToken)
    {
        var deposit = Transaction.PostBankDeposit(
            line.TenantId,
            account.Id,
            account.Currency,
            line.Amount,
            now,
            TransactionSource.BankFeed(line.PublicId),
            new BankReceipt(line.ValueDate, line.ReferenceText, line.PayerName, line.PayerAccountMasked));
        if (createdBy is not null)
        {
            deposit.CreatedBy = createdBy;
            foreach (var leg in deposit.CashLegs)
            {
                leg.CreatedBy = createdBy;
            }
        }

        db.Transactions.Add(deposit);
        await db.SaveChangesAsync(cancellationToken);
        return deposit;
    }

    /// <summary>The Accounts of the tenant that carry one of <paramref name="candidates"/> (R16).</summary>
    public static async Task<List<Account>> AccountsAsync(
        IApplicationDbContext db,
        int tenantId,
        IReadOnlyList<DepositReference> candidates,
        CancellationToken cancellationToken)
    {
        var accounts = new List<Account>();
        foreach (var candidate in candidates)
        {
            var account = await db.Accounts.IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    row => row.TenantId == tenantId && row.DepositReference! == candidate,
                    cancellationToken);
            if (account is not null && accounts.All(found => found.Id != account.Id))
            {
                accounts.Add(account);
            }
        }

        return accounts;
    }

    public static Task<User> CustomerAsync(IApplicationDbContext db, Account account, CancellationToken cancellationToken) =>
        db.Users.IgnoreQueryFilters().SingleAsync(row => row.Id == account.CustomerId, cancellationToken);

    public static bool IsDisabled(User customer) => customer.Status == UserStatus.Disabled;
}
