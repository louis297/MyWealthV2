using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Transactions;

internal static class PortfolioTradeLock
{
    public static async Task RejectIfAssignedAsync(
        IApplicationDbContext db,
        int accountId,
        TransactionType type,
        CancellationToken cancellationToken)
    {
        if (type is not (TransactionType.Buy or TransactionType.Sell or TransactionType.StockIn
            or TransactionType.StockOut or TransactionType.Split or TransactionType.Bonus
            or TransactionType.Scrip or TransactionType.OpeningHoldings))
        {
            return;
        }

        var assigned = await db.AccountPortfolioAssignments.AnyAsync(
            row => row.AccountId == accountId && row.Status == AssignmentStatus.Active,
            cancellationToken);
        if (!assigned)
        {
            return;
        }

        throw new ValidationException([
            new ValidationFailure(
                string.Empty,
                "This account is assigned to a portfolio. Manual security trades are not allowed.")
        ]);
    }
}
