using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.StatementLines;

/// <summary>The return payments of statement lines (docs/features/bank-returns.md R5).</summary>
internal static class StatementLineReturns
{
    /// <summary>The line's open (<c>Pending</c> or <c>Accepted</c>) return payment, tracked; a line has at most one.</summary>
    public static Task<OutgoingPayment?> OpenPaymentAsync(IApplicationDbContext db, int lineId, CancellationToken cancellationToken) =>
        db.OutgoingPayments.IgnoreQueryFilters()
            .Where(row => row.Kind == OutgoingPaymentKind.ReceiptReturn
                          && row.ReturnOfLineId == lineId
                          && (row.Status == OutgoingPaymentStatus.Pending || row.Status == OutgoingPaymentStatus.Accepted))
            .SingleOrDefaultAsync(cancellationToken);
}
