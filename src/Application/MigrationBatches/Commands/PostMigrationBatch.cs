using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Migrations;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.MigrationBatches.Commands;

/// <summary>Migration batches R20–R24: a second TenantAdmin posts a <c>Submitted</c> batch, all entries or none.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class PostMigrationBatchCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class PostMigrationBatchCommandValidator : AbstractValidator<PostMigrationBatchCommand>
{
    public PostMigrationBatchCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.TenantId).Must(MigrationBatchBodyRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class PostMigrationBatchCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    ICurrencyCatalog catalog,
    TimeProvider time) : IRequestHandler<PostMigrationBatchCommand>
{
    public async Task Handle(PostMigrationBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await MigrationBatchRules.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken, withBody: true);
        var actor = MigrationBatchActionRules.ActorId(currentUser);
        if (batch.Status != MigrationBatchStatus.Submitted)
        {
            throw MigrationBatchRules.StatusError(batch.StatusMessage("posted"));
        }

        MigrationBatchRules.Guard(() => batch.EnsureNotSubmitter(actor));

        // R21: one database transaction on webapi; no outside party is called.
        await db.ExecuteInTransactionAsync(async token =>
        {
            foreach (var accountId in batch.Entries.Select(entry => entry.AccountId).Distinct().Order())
            {
                await db.LockAccountAsync(accountId, token);
            }

            // Custody-modes R12: the clock is read once, after the locks; every Transaction has this bookedAt.
            var now = time.GetUtcNow();
            var state = await MigrationBatchValidator.ValidateAsync(db, catalog, batch, token);
            if (!state.Result.IsValid)
            {
                throw new MigrationBatchNotValidException(state.Result.Issues.Count);
            }

            var source = TransactionSource.Migration(batch.PublicId);
            var origin = new MigrationOrigin(batch.AsOfDate, batch.PreviousProvider);
            var written = new List<(MigrationBatchEntry Entry, Transaction? Opening, Transaction? Holdings)>();
            foreach (var entry in batch.Entries.OrderBy(entry => entry.Position))
            {
                var account = state.Accounts[entry.AccountId];
                Transaction? opening = null;
                Transaction? holdings = null;
                if (entry.Cash is { } cash)
                {
                    opening = Transaction.PostMigratedCash(batch.TenantId, account.Id, account.Currency, cash, now,
                        entry.ExternalReference, source, origin);
                    db.Transactions.Add(opening);
                }

                if (entry.Holdings.Count > 0)
                {
                    holdings = Transaction.PostMigratedHoldings(batch.TenantId, account.Id, account.Type, now,
                        entry.ExternalReference, source, origin,
                        entry.Holdings.Select(holding => OpeningSecurityLeg.Create(holding.InstrumentId, holding.Quantity,
                            holding.Cost, state.Instruments[holding.InstrumentId].QuoteCurrency.Trim())));
                    db.Transactions.Add(holdings);
                }

                written.Add((entry, opening, holdings));
            }

            await db.SaveChangesAsync(token);

            // Holdings R14: the projection follows in the same transaction.
            foreach (var (entry, _, holdings) in written.Where(row => row.Holdings is not null))
            {
                await HoldingProjection.SyncAsync(db, batch.TenantId, entry.AccountId,
                    entry.Holdings.Select(holding => holding.InstrumentId), token);
            }

            foreach (var (entry, opening, holdings) in written)
            {
                entry.RecordTransactions(opening?.Id, holdings?.Id);
            }

            MigrationBatchRules.Guard(() => batch.MarkPosted(actor, now));
            await MigrationBatchRules.SaveAsync(db, token);
        }, cancellationToken);
    }
}
