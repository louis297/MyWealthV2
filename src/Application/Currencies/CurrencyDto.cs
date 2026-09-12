using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Currencies;

public sealed record CurrencyDto(string Code, string Name, int DecimalPlaces, bool IsEnabled)
{
    public static CurrencyDto From(Currency currency) =>
        new(currency.Code, currency.Name, currency.DecimalPlaces, currency.IsEnabled);
}
