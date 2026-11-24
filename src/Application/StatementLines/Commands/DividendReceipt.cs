using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Domain.Enums;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.StatementLines.Commands;

/// <summary>
/// Dividends R26: a TenantAdmin matches an unmatched receipt to a dividend that waits for its payment, in its quote
/// currency, with a note. The amount is not checked: the person accepts the difference, and it stays visible on the
/// dividend and in the bank reconciliation. Nothing is posted here; the line and the dividend change together.
/// </summary>
internal static class DividendReceipt
{
    public const string WaitingMessage = "Only a dividend that waits for its payment can take a receipt.";

    public static async Task MatchAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        TimeProvider time,
        Guid lineId,
        Guid corporateActionId,
        string? rowVersion,
        string note,
        CancellationToken cancellationToken)
    {
        if (currentUser.Role is not { } role || !RolePermissions.Has(role, Policies.CorporateActionsManage))
        {
            throw new ForbiddenAccessException();
        }

        var line = await StatementLineReader.FindAsync(db, currentUser, lineId, cancellationToken);
        StatementLineActions.CheckRowVersion(line, rowVersion);
        StatementLineActions.Guard(line.EnsureCanMatchByHand);
        var actor = StatementLineActions.ActorId(currentUser);
        var action = await CorporateActionReader.FindAsync(db, currentUser, corporateActionId, cancellationToken);
        if (action.Kind != CorporateActionKind.Dividend || action.Status != CorporateActionStatus.Effective
            || action.ReceiptLineId is not null)
        {
            throw Error(WaitingMessage);
        }

        var currency = (await db.Instruments.AsNoTracking()
            .Where(row => row.Id == action.InstrumentId)
            .Select(row => row.QuoteCurrency)
            .SingleAsync(cancellationToken)).Trim();
        if (!string.Equals(currency, line.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw Error($"This dividend is paid in {currency} and this receipt is in {line.Currency.Trim()}.");
        }

        var now = time.GetUtcNow();
        line.MatchToDividend(action.Id, note, actor, now);
        action.TakeReceipt(line.Id, line.Amount);
        await PayDividend.TryAsync(db, action, now, cancellationToken);
        await StatementLineActions.SaveAsync(db, cancellationToken);
    }

    private static ValidationException Error(string message) =>
        new([new ValidationFailure(nameof(MatchStatementLineCommand.CorporateActionId), message)]);
}
