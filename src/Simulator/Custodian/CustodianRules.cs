using MyWealthV2.Simulator.Bank;

namespace MyWealthV2.Simulator.Custodian;

/// <summary>The field rules of the simulator custodian (in-kind-transfers R61–R66).</summary>
public static class CustodianRules
{
    public const int InstructionIdMaxLength = 100;
    public const int SymbolMaxLength = 32;
    public const int CounterpartyNameMaxLength = 100;
    public const int HolderNameMaxLength = 140;
    public const int FreeTextMaxLength = 140;
    public const int CounterpartyAccountMaxLength = 34;

    /// <summary>R35, R64: the reasons a transfer may be rejected with.</summary>
    public static readonly IReadOnlyList<string> RejectReasons = ["UnknownInstrument", "InsufficientPosition", "CounterpartyRefused", "Refused"];

    public static bool IsQuantity(decimal? quantity) => quantity is > 0m && decimal.Round(quantity.Value, 8) == quantity.Value;

    public static bool IsSymbol(string? symbol) => symbol?.Trim().Length is >= 1 and <= SymbolMaxLength;

    public static bool IsDirection(string? direction) => direction is "In" or "Out";

    public static int Direction(string direction) => direction == "In" ? CustodianMovement.InDirection : CustodianMovement.OutDirection;

    public static bool IsAccountNumber(string? number) => BankRules.IsAccountNumber(number);

    public static bool IsCurrency(string? currency) => BankRules.IsCurrency(currency);

    public static bool IsName(string? name) => BankRules.IsName(name);

    /// <summary>Free text cut to its length (R66 optional fields), or null.</summary>
    public static string? Text(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
