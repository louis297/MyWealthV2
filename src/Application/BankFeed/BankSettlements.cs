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
}
