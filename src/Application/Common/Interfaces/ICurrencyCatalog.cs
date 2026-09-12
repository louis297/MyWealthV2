using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Common.Interfaces;

public interface ICurrencyCatalog
{
    Currency? TryGet(string code);

    IReadOnlyList<Currency> List(bool enabledOnly = false);

    bool IsEnabled(string code);

    Task ReloadAsync(CancellationToken cancellationToken = default);
}
