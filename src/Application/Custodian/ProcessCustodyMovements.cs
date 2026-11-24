using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Custody;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Application.Custodian;

/// <summary>
/// The processing step of <c>custodian-sync:{connection}</c> (docs/features/in-kind-transfers.md R14–R17, R36–R40;
/// ADR 0021 rule 8): every <c>Received</c> custody movement of the connection, oldest first, each in one database
/// transaction that also sets its status and is guarded by its row version. The one place a transfer is posted.
/// </summary>
public sealed class ProcessCustodyMovements(
    IApplicationDbContext db,
    TimeProvider time,
    ILogger<ProcessCustodyMovements> logger)
{
    public async Task RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        var ids = await db.CustodyMovements.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Connection == connection && row.Status == CustodyMovementStatus.Received)
            .OrderBy(row => row.ReceivedAt)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            db.ClearTrackedChanges();
            try
            {
                await db.ExecuteInTransactionAsync(token => ProcessAsync(id, token), cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // R14: another writer changed the movement or its transfer; the next run sees it again if it is still Received.
                logger.LogWarning("Custody movement {MovementId} changed while it was processed; it is skipped.", id);
            }
        }
    }

    private async Task ProcessAsync(int id, CancellationToken cancellationToken)
    {
        var movement = await db.CustodyMovements.IgnoreQueryFilters().SingleAsync(row => row.Id == id, cancellationToken);
        if (movement.Status != CustodyMovementStatus.Received)
        {
            return;
        }

        // R15 rows 1 and 2: an instruction id names a transfer of the movement's Tenant, or no transfer at all.
        if (movement.InstructionId is not null)
        {
            var transfer = Guid.TryParse(movement.InstructionId, out var transferId)
                ? await db.InKindTransfers.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(row => row.PublicId == transferId && row.TenantId == movement.TenantId, cancellationToken)
                : null;
            if (transfer is null)
            {
                movement.Leave(CustodyUnmatchedReason.UnknownInstruction);
            }
            else
            {
                await ConfirmTransferAsync(movement, transfer, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // R15 rows 3–6 are processed by a later step of this slice; the movement waits.
    }

    /// <summary>R36–R40: the movement confirms its transfer and posts its Transaction, or waits in suspense.</summary>
    private async Task ConfirmTransferAsync(CustodyMovement movement, InKindTransfer transfer, CancellationToken cancellationToken)
    {
        if (!transfer.WasAsked)
        {
            movement.Leave(CustodyUnmatchedReason.TransferNotOpen);
            return;
        }

        var direction = transfer.Direction == TransferDirection.In ? CustodyMovementDirection.In : CustodyMovementDirection.Out;
        if (movement.SafekeepingAccountId != transfer.SafekeepingAccountId
            || movement.Direction != direction
            || movement.InstrumentId != transfer.InstrumentId
            || movement.Quantity != transfer.Quantity)
        {
            movement.Leave(CustodyUnmatchedReason.TransferMismatch);
            return;
        }

        // R36: posting R22, the Account lock; R40: whatever the state of the Tenant, the Customer or the Instrument.
        await db.LockAccountAsync(transfer.AccountId, cancellationToken);
        var account = await db.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == transfer.AccountId, cancellationToken);
        if (account.Status == AccountStatus.Closed)
        {
            movement.Leave(CustodyUnmatchedReason.AccountClosed);
            return;
        }

        var holding = await db.Holdings.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(row => row.AccountId == account.Id && row.InstrumentId == transfer.InstrumentId, cancellationToken);
        if (transfer.Direction == TransferDirection.Out && (holding?.Quantity ?? 0m) < transfer.Quantity)
        {
            movement.Leave(CustodyUnmatchedReason.InsufficientHolding);
            return;
        }

        var now = time.GetUtcNow();
        var source = TransactionSource.InKindTransfer(transfer.PublicId);
        var origin = new InKindTransferOrigin(transfer.PublicId, transfer.CounterpartyName, transfer.CounterpartyAccount,
            transfer.ChangesBeneficialOwner, transfer.AcquiredOn, movement.SettlementDate, movement.OutsideId);
        var currency = transfer.Currency.Trim();
        var posting = transfer.Direction == TransferDirection.In
            ? Transaction.PostTransferIn(account.TenantId, account.Id, account.Type, transfer.InstrumentId, transfer.Quantity,
                transfer.Cost!.Value, currency, now, source, origin)
            : Transaction.PostTransferOut(account.TenantId, account.Id, account.Type, transfer.InstrumentId, transfer.Quantity,
                currency, now, source, origin, holding!.Quantity, holding.CostAmount);
        posting.CreatedBy = SystemActor.Name;
        posting.SecurityLegs.ForEach(leg => leg.CreatedBy = SystemActor.Name);
        db.Transactions.Add(posting);
        await db.SaveChangesAsync(cancellationToken);

        // Holdings R14: the projection follows in the same transaction.
        await HoldingProjection.SyncAsync(db, account.TenantId, account.Id, [transfer.InstrumentId], cancellationToken, SystemActor.Name);
        transfer.Complete(posting.Id, now);
        movement.MatchToTransfer(account.Id, posting.Id, transfer.Id);
    }
}
