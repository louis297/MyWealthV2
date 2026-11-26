using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Valuations.Runs;

/// <summary>Valuation-run R3, R5, R6, R8–R12: one Custodial account through a date, one day at a time.</summary>
public class StoreAccountValuations(IApplicationDbContext db, IValueAccount valueAccount, TimeProvider time) : IStoreAccountValuations
{
    public Task<StoredDays> ValueThroughAsync(
        Account account, DateOnly throughDate, ValuationRun run, CancellationToken cancellationToken)
    {
        _ = (db, valueAccount, time);
        throw new NotImplementedException();
    }
}
