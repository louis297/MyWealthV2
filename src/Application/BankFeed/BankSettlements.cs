using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// Trade-settlement R18–R21: the statement lines that carry the cash of a trade settlement. A matched line names the
/// trade settlement and posts no Transaction: the fills were posted when they were reported.
/// </summary>
internal sealed class BankSettlements(IApplicationDbContext db, TimeProvider time)
{
    /// <summary>R18: a debit line whose end-to-end id is the instruction id of a settlement payment of the line's
    /// Tenant and client money account. True when the line was this case.</summary>
    public async Task<bool> ProcessPaidAsync(BankStatementLine line, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(line.EndToEndId, out var instruction))
        {
            return false;
        }

        var payment = await db.OutgoingPayments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.InstructionPublicId == instruction
                                         && row.Kind == OutgoingPaymentKind.TradeSettlement
                                         && row.TenantId == line.TenantId
                                         && row.ClientMoneyAccountId == line.ClientMoneyAccountId, cancellationToken);
        if (payment is null)
        {
            return false;
        }

        var settlement = await db.TradeSettlements.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == payment.TradeSettlementId, cancellationToken);
        var asked = payment.Status == OutgoingPaymentStatus.Accepted
                    || (payment.Status == OutgoingPaymentStatus.Pending && payment.Attempts > 0);
        if (!asked || line.Amount != payment.Amount || settlement.Status != TradeSettlementStatus.AwaitingPayment)
        {
            line.MarkPaymentMismatch();
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var now = time.GetUtcNow();
        payment.MarkPaid();
        settlement.Complete(now);
        line.MarkSettlement(settlement.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>R19: a credit transfer with no <c>returnOf</c> whose reference text, trimmed and without regard to
    /// case, is the broker settlement id of a trade settlement of the line's Tenant and client money account. It is
    /// settlement cash, never a deposit. True when the line was this case.</summary>
    public async Task<bool> ProcessReceiptAsync(BankStatementLine line, CancellationToken cancellationToken)
    {
        var reference = line.ReferenceText?.Trim();
        if (line.Direction != BankLineDirection.Credit
            || line.Kind != StatementLineKind.Transfer
            || line.ReturnOfOutsideId is not null
            || string.IsNullOrEmpty(reference))
        {
            return false;
        }

        var key = reference.ToUpperInvariant();
        var settlement = await db.TradeSettlements.IgnoreQueryFilters()
            .Where(row => row.TenantId == line.TenantId
                          && row.ClientMoneyAccountId == line.ClientMoneyAccountId
                          && row.BrokerSettlementId.ToUpper() == key)
            .OrderBy(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (settlement is null)
        {
            return false;
        }

        if (settlement.Status != TradeSettlementStatus.AwaitingReceipt || line.Amount != settlement.NetAmount)
        {
            line.MarkUnmatched(UnmatchedReason.SettlementMismatch);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var now = time.GetUtcNow();
        settlement.Complete(now);
        line.MarkSettlement(settlement.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>R20: a credit line that returns the debit line of a <c>Paid</c> settlement payment. True when the
    /// line was this case.</summary>
    public async Task<bool> ProcessReturnAsync(BankStatementLine line, CancellationToken cancellationToken)
    {
        var returned = await BankRecalls.ReturnedLineAsync(db, line, cancellationToken);
        if (returned is not { Direction: BankLineDirection.Debit, MatchMethod: StatementLineMatchMethod.Settlement, TradeSettlementId: int settlementId }
            || !Guid.TryParse(returned.EndToEndId, out var instruction))
        {
            return false;
        }

        var settlement = await db.TradeSettlements.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == settlementId, cancellationToken);
        var payment = await db.OutgoingPayments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.InstructionPublicId == instruction && row.TradeSettlementId == settlementId, cancellationToken);
        if (payment is not { Status: OutgoingPaymentStatus.Paid } || line.Amount != payment.Amount
            || settlement.Status != TradeSettlementStatus.Completed)
        {
            line.MarkPaymentMismatch();
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        payment.MarkReturned();
        settlement.FailPayment(TradeSettlementFailure.PaymentReturned, line.ReturnReason);
        line.MarkSettlementReturn(settlement.Id, returned.Id, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
