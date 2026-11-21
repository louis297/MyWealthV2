using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// Bank-returns R7: the bank's debit line of a receipt return closes it. The caller runs it inside the line's own
/// database transaction.
/// </summary>
internal sealed class BankReturns(IApplicationDbContext db, TimeProvider time)
{
    /// <summary>True when the debit line's end-to-end id names a return payment of the line's Tenant and client money
    /// account that the bank was asked to send, and the line was handled; false otherwise.</summary>
    public async Task<bool> ProcessReturnPaidAsync(BankStatementLine line, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(line.EndToEndId, out var instruction))
        {
            return false;
        }

        var payment = await db.OutgoingPayments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.InstructionPublicId == instruction
                                         && row.Kind == OutgoingPaymentKind.ReceiptReturn
                                         && row.TenantId == line.TenantId
                                         && row.ClientMoneyAccountId == line.ClientMoneyAccountId, cancellationToken);
        if (payment is null)
        {
            return false;
        }

        var returned = await db.BankStatementLines.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == payment.ReturnOfLineId, cancellationToken);
        var asked = payment.Status == OutgoingPaymentStatus.Accepted
                    || (payment.Status == OutgoingPaymentStatus.Pending && payment.Attempts > 0);
        if (!asked || returned.Status is not (BankStatementLineStatus.ReturnSent or BankStatementLineStatus.ReturnRequested))
        {
            return false;
        }

        if (line.Amount != payment.Amount)
        {
            line.MarkPaymentMismatch();
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        // Nothing is written to the ledger: the receipt was never a Customer's.
        returned.MarkReturned();
        payment.MarkPaid();
        line.MarkReturnPaid(returned.Id, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
