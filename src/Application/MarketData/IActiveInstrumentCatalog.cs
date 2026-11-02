namespace MyWealthV2.Application.MarketData;

public interface IActiveInstrumentCatalog
{
    Task<IReadOnlyList<ActiveInstrument>> ListActiveAsync(CancellationToken cancellationToken = default);
}
