namespace MyWealthV2.Infrastructure.Data;

public static class TestSeed
{
    public static Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        return Task.CompletedTask;
    }

    public static Task SeedAsync(
        ApplicationDbContext db,
        MyWealthV2.Infrastructure.MarketData.MarketDataMock prices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(prices);
        return Task.CompletedTask;
    }
}
