using Microsoft.EntityFrameworkCore;
using MyWealthV2.Simulator.Data;

namespace MyWealthV2.Simulator.Bank;

/// <summary>Bank-returns R12, R14: which credit lines the simulator bank can still send back or recall.</summary>
public static class BankReturns
{
    /// <summary>The receipt <paramref name="outsideId"/> of the account, when it can still be returned: a credit line
    /// that is not itself a return, with no recall and no return payment that was not rejected.</summary>
    public static async Task<BankLine?> ReturnableAsync(
        SimulatorDbContext db,
        int bankAccountId,
        string outsideId,
        CancellationToken cancellationToken)
    {
        var line = await db.BankLines.AsNoTracking()
            .SingleOrDefaultAsync(row => row.BankAccountId == bankAccountId && row.OutsideId == outsideId, cancellationToken);
        return line is not null && await IsReturnableAsync(db, line, cancellationToken) ? line : null;
    }

    public static async Task<bool> IsReturnableAsync(SimulatorDbContext db, BankLine line, CancellationToken cancellationToken) =>
        line.Direction == BankLine.CreditDirection
        && line.ReturnOfLineId is null
        && !await db.BankLines.AnyAsync(row => row.ReturnOfLineId == line.Id, cancellationToken)
        && !await db.BankPayments.AnyAsync(
            row => row.ReturnOfLineId == line.Id && row.Status != BankPaymentStatus.Rejected, cancellationToken);
}
