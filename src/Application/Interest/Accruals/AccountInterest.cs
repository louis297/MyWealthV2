using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Interest.Accruals;

/// <summary>Interest-run R16–R22, R28: accrue one account through a day, then post what is due.</summary>
public class AccountInterest : IAccountInterest
{
    public Task<AccountInterestResult> AccrueAndPostAsync(
        Account account,
        DateOnly asOfDate,
        DateOnly postingDate,
        Guid runPublicId,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
