using Microsoft.Data.SqlClient;

namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Extra environments for fixtures marked <c>[Parallelizable]</c>. Each one has its own database
/// in the same SQL Server container, named after the default database with a "-pN" suffix.
/// An environment is created the first time a parallel fixture needs one and none is free, so a
/// filtered run that hits no parallel fixture creates none.
/// </summary>
internal sealed class TestEnvironmentPool(string defaultConnectionString) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Stack<TestEnvironment> _free = new();
    private readonly List<TestEnvironment> _all = [];

    public async Task<TestEnvironment> LeaseAsync(CancellationToken cancellationToken)
    {
        int number;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_free.TryPop(out var free))
            {
                return free;
            }

            number = _all.Count + 1;
            // Reserve the slot so a concurrent lease picks the next database name.
            _all.Add(null!);
        }
        finally
        {
            _gate.Release();
        }

        var builder = new SqlConnectionStringBuilder(defaultConnectionString);
        builder.InitialCatalog += "-p" + number;
        var environment = await TestEnvironment.CreateAsync(builder.ConnectionString, cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _all[number - 1] = environment;
        }
        finally
        {
            _gate.Release();
        }

        return environment;
    }

    public void Return(TestEnvironment environment)
    {
        _gate.Wait();
        try
        {
            _free.Push(environment);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var environment in _all.Where(environment => environment is not null))
        {
            await environment.DisposeAsync();
        }

        _gate.Dispose();
    }
}
