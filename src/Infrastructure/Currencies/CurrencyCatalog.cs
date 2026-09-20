using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MyWealthV2.Infrastructure.Currencies;

public sealed class CurrencyCatalog(IServiceScopeFactory scopes) : ICurrencyCatalog
{
    private readonly object _gate = new();
    private IReadOnlyDictionary<string, Currency> _rows =
        new Dictionary<string, Currency>(StringComparer.OrdinalIgnoreCase);

    public Currency? TryGet(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        lock (_gate)
        {
            return _rows.TryGetValue(code.Trim().ToUpperInvariant(), out var row) ? row : null;
        }
    }

    public IReadOnlyList<Currency> List(bool enabledOnly = false)
    {
        List<Currency> snapshot;
        lock (_gate)
        {
            snapshot = _rows.Values.ToList();
        }

        IEnumerable<Currency> rows = snapshot;
        if (enabledOnly)
        {
            rows = rows.Where(currency => currency.IsActive);
        }

        return rows.OrderBy(currency => currency.Code, StringComparer.Ordinal).ToList();
    }

    public bool IsEnabled(string code) => TryGet(code)?.IsActive == true;

    public bool IsActive(string code) => false;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var loaded = await db.Set<Currency>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        lock (_gate)
        {
            _rows = loaded.ToDictionary(currency => currency.Code, StringComparer.OrdinalIgnoreCase);
        }
    }
}
