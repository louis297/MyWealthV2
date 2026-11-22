using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Interest.Accruals;

/// <summary>
/// Interest-run R16–R26: the one place interest is accrued and posted, for one account. The caller holds the Account
/// lock inside a database transaction (posting R22), as every posting does.
/// </summary>
public interface IAccountInterest
{
    /// <summary>
    /// Accrues <paramref name="account"/> through <paramref name="asOfDate"/> (R19) and posts every unposted accrual row
    /// with a day on or before <paramref name="postingDate"/> (R21, R22) as one <c>Interest</c> with source
    /// <c>InterestRun</c> and <paramref name="runPublicId"/> as source id. External, Closed and other accounts the
    /// caller should not pass are its own concern; an External account is left as it is.
    /// </summary>
    Task<AccountInterestResult> AccrueAndPostAsync(
        Account account,
        DateOnly asOfDate,
        DateOnly postingDate,
        Guid runPublicId,
        CancellationToken cancellationToken);
}

/// <summary>The rows the call stored and the posting it made, if any.</summary>
public sealed record AccountInterestResult(int RowsAdded, Transaction? Posting);
