using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.ValueObjects;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.StatementLines.Commands;

/// <summary>Deposit matching R30, R33: undo a match by reversing its deposit, while the account holds the cash.</summary>
[Authorize(Policy = Policies.StatementLinesManage)]
public sealed class UnmatchStatementLineCommand : IRequest
{
    public const string CashGoneMessage =
        "The account no longer holds this cash, so the deposit cannot be undone. Correct it with an adjustment.";

    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

public class UnmatchStatementLineCommandValidator : AbstractValidator<UnmatchStatementLineCommand>
{
    public UnmatchStatementLineCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note).Must(StatementLineActions.IsRequiredNote).WithMessage(StatementLineActions.NoteMessage);
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public class UnmatchStatementLineCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider time,
    ICurrencyCatalog currencies)
    : IRequestHandler<UnmatchStatementLineCommand>
{
    public async Task Handle(UnmatchStatementLineCommand request, CancellationToken cancellationToken)
    {
        var line = await StatementLineReader.FindAsync(db, currentUser, request.Id, cancellationToken);
        StatementLineActions.CheckRowVersion(line, request.RowVersion);
        StatementLineActions.Guard(line.EnsureCanUnmatch);
        var actor = StatementLineActions.ActorId(currentUser);
        if (line.MatchMethod == StatementLineMatchMethod.Migration)
        {
            await MigrationBatchFunding.UnmatchAsync(db, line, request.Note!, actor, time.GetUtcNow(), cancellationToken);
            return;
        }

        var accountId = line.MatchedAccountId!.Value;
        var depositId = line.MatchedTransactionId!.Value;

        await db.ExecuteInTransactionAsync(async token =>
        {
            await db.LockAccountAsync(accountId, token);
            var account = await db.Accounts.AsNoTracking().SingleAsync(row => row.Id == accountId, token);
            await StatementLinePosting.EnsureCanPostAsync(db, account, token);

            var deposit = await db.Transactions
                .Include(row => row.CashLegs)
                .Include(row => row.BankReceiptDetail)
                .SingleAsync(row => row.Id == depositId, token);
            var cash = await TransactionCash.SumAsync(db, accountId, token);
            var now = time.GetUtcNow();
            Domain.Entities.Transaction reversal;
            try
            {
                // Posting R9 holds for flows too: the Reversal may not take the balance below zero (R30).
                reversal = deposit.Reverse(
                    TransactionSource.BankFeed(line.PublicId), now, new Dictionary<int, decimal> { [accountId] = cash });
            }
            catch (DomainException)
            {
                throw new ValidationException([
                    new ValidationFailure(string.Empty, UnmatchStatementLineCommand.CashGoneMessage)
                ]);
            }

            db.Transactions.Add(reversal);
            await db.SaveChangesAsync(token);

            // Withdrawals R36: the undo may not take the cash below what open withdrawals reserve.
            await AvailableCashGuard.EnsureAsync(db, currencies, account, await TransactionCash.SumAsync(db, accountId, token), token);
            line.Unmatch(request.Note!, actor, now);
            await StatementLineActions.SaveAsync(db, token);
        }, cancellationToken);
    }
}
