using FluentValidation.Results;
using MyWealthV2.Application.AccountClosures;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.InKindTransfers;

/// <summary>The account, instrument and safekeeping account checks of in-kind-transfers R22–R24 and R31.</summary>
internal static class InKindTransferRules
{
    public const string NotCustodialMessage = "Securities can be transferred in kind only on a Custodial account.";
    public const string ClosedMessage = "Cannot post a transaction while the account is closed. Reopen the account first.";
    public const string AssignedMessage = "This account follows a portfolio. Unassign the portfolio before transferring securities.";
    public const string InstrumentNotFoundMessage = "Instrument was not found.";
    public const string InstrumentDisabledMessage = "Instrument is disabled.";

    public static string NoSafekeepingMessage(string currency, string symbol) =>
        $"The firm has no safekeeping account for securities quoted in {currency}, so {symbol} cannot be transferred.";

    public static string CurrencyMessage(string symbol, string quote, string accountCurrency) =>
        $"{symbol} is quoted in {quote} and this account is in {accountCurrency}. Transfer it into a {quote} Custodial account.";

    /// <summary>R22: Open, not closing, and its Customer not Disabled. Custodial is checked by the request only.</summary>
    public static async Task EnsureAccountTakesTransfersAsync(IApplicationDbContext db, Account account, CancellationToken cancellationToken)
    {
        if (account.Status == AccountStatus.Closed)
        {
            throw Error(string.Empty, ClosedMessage);
        }

        await AccountClosing.EnsureNotClosingAsync(db, account.Id, cancellationToken);
        var customer = await db.Users.AsNoTracking().SingleAsync(row => row.Id == account.CustomerId, cancellationToken);
        if (customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException("user", customer.PublicId, "User is disabled",
                "Cannot transfer securities while the customer is disabled. Enable the customer first.");
        }
    }

    /// <summary>R24, R45: true when the account has an open in-kind transfer, in either direction.</summary>
    public static Task<bool> HasOpenTransferAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken) =>
        db.InKindTransfers.IgnoreQueryFilters().AnyAsync(
            row => row.AccountId == accountId
                   && (row.Status == InKindTransferStatus.Requested || row.Status == InKindTransferStatus.Approved
                       || row.Status == InKindTransferStatus.Sent),
            cancellationToken);

    /// <summary>R24: true when the account has an Active portfolio assignment.</summary>
    public static Task<bool> IsAssignedAsync(IApplicationDbContext db, int accountId, CancellationToken cancellationToken) =>
        db.AccountPortfolioAssignments.AnyAsync(row => row.AccountId == accountId && row.Status == AssignmentStatus.Active, cancellationToken);

    /// <summary>R6, R23, R31: the Tenant's active safekeeping account in <paramref name="currency"/> whose connection is
    /// configured on this host, else the R23 refusal at <c>errors.InstrumentId</c>.</summary>
    public static async Task<SafekeepingAccount> SafekeepingAccountAsync(
        IApplicationDbContext db, ICustodianConnections connections, int tenantId, string currency, string symbol,
        CancellationToken cancellationToken)
    {
        var row = await db.SafekeepingAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(account => account.TenantId == tenantId && account.IsActive && account.Currency == currency, cancellationToken);
        if (row is null || !connections.IsConfigured(row.CustodianConnection))
        {
            throw Error("InstrumentId", NoSafekeepingMessage(currency, symbol));
        }

        return row;
    }

    public static ValidationException Error(string property, string message) => new([new ValidationFailure(property, message)]);
}
