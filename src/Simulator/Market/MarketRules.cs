using MyWealthV2.Simulator.Bank;

namespace MyWealthV2.Simulator.Market;

/// <summary>The field rules of the simulator market (simulated-instruments R15).</summary>
public static class MarketRules
{
    public const string Suffix = ".SIM";
    public const int SymbolMinLength = 5;
    public const int SymbolMaxLength = 32;
    public const int NameMaxLength = 200;
    public const int PriceMaxDecimalPlaces = 4;
    public const decimal DefaultDailyMovePercent = 2m;
    public const decimal MaxDailyMovePercent = 20m;

    /// <summary>5–32 letters, digits, <c>.</c> and <c>-</c> after trim, ending in <c>.SIM</c> in any case.</summary>
    public static bool IsSymbol(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length is >= SymbolMinLength and <= SymbolMaxLength
               && trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-')
               && trimmed.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The symbol as stored: trimmed, upper case.</summary>
    public static string Symbol(string value) => value.Trim().ToUpperInvariant();

    public static bool IsName(string? value) => value?.Trim().Length is >= 1 and <= NameMaxLength;

    public static bool IsCurrency(string? value) => BankRules.IsCurrency(value);

    public static bool IsPrice(decimal? price) =>
        price is > 0 && decimal.Round(price.Value, PriceMaxDecimalPlaces) == price.Value;

    public static bool IsDailyMove(decimal? percent) =>
        percent is >= 0 and <= MaxDailyMovePercent && decimal.Round(percent.Value, 2) == percent.Value;
}
