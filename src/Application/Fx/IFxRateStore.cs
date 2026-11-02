namespace MyWealthV2.Application.Fx;

public interface IFxRateStore
{
    Task UpsertAsync(
        IReadOnlyList<FxRateQuote> quotes,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken = default);

    Task<bool> HasAsOfDateForEveryPairAsync(
        IReadOnlyList<string> currencies,
        DateOnly asOf,
        CancellationToken cancellationToken = default);
}
