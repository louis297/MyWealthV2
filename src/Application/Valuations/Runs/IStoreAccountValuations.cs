using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>The one place a valuation is stored (valuation-run §7).</summary>
public interface IStoreAccountValuations
{
    /// <summary>R3, R5, R6, R8–R12: values and stores <paramref name="account"/> for each day after its valued-through
    /// date up to the earlier of <paramref name="throughDate"/> and its last day, one database transaction per day.
    /// A failure stops the account at that day and is returned, not thrown.</summary>
    Task<StoredDays> ValueThroughAsync(Account account, DateOnly throughDate, ValuationRun run, CancellationToken cancellationToken);
}

/// <summary>What <see cref="IStoreAccountValuations.ValueThroughAsync"/> did for one account: whether it had a day to
/// value, the days it stored and how many of them were incomplete, and the day and error it stopped at.</summary>
public sealed record StoredDays(bool HadDays, int Valued, int Incomplete, DateOnly? FailedDay = null, Exception? Failure = null)
{
    public static readonly StoredDays None = new(false, 0, 0);
}
