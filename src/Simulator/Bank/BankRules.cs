using System.Xml;

namespace MyWealthV2.Simulator.Bank;

/// <summary>The field rules of the simulator bank (deposit-matching R38, R41).</summary>
public static class BankRules
{
    public const int AccountNumberMaxLength = 34;
    public const int NameMaxLength = 140;
    public const int FreeTextMaxLength = 140;
    public const int AmountMaxDecimalPlaces = 4;
    public static readonly TimeSpan MaxAvailableAfter = TimeSpan.FromDays(30);

    /// <summary>1–34 letters, digits, spaces and hyphens, no two spaces in a row (after trim).</summary>
    public static bool IsAccountNumber(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length is >= 1 and <= AccountNumberMaxLength
               && trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is ' ' or '-')
               && !trimmed.Contains("  ", StringComparison.Ordinal);
    }

    /// <summary>Three upper-case letters; the simulator holds no currency catalog.</summary>
    public static bool IsCurrency(string? value) =>
        value is { Length: 3 } && value.All(char.IsAsciiLetterUpper);

    public static bool IsName(string? value) => value?.Trim().Length is >= 1 and <= NameMaxLength;

    public static bool IsAmount(decimal? amount) =>
        amount is > 0 && decimal.Round(amount.Value, AmountMaxDecimalPlaces) == amount.Value;

    /// <summary>An ISO 8601 duration from <c>PT0S</c> to <c>P30D</c>, or null when it is not one.</summary>
    public static TimeSpan? AvailableAfter(string? value)
    {
        if (value is null)
        {
            return TimeSpan.Zero;
        }

        try
        {
            var duration = XmlConvert.ToTimeSpan(value);
            return duration >= TimeSpan.Zero && duration <= MaxAvailableAfter ? duration : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Withdrawals R49: the platform's instruction id, opaque, 1–100 characters (R57).</summary>
    public const int InstructionIdMaxLength = 100;

    /// <summary>Withdrawals R49: the reference a payment carries to the payee's statement.</summary>
    public const int PaymentReferenceMaxLength = 18;

    /// <summary>Withdrawals R34: the reasons a bank gives for refusing a payment.</summary>
    /// <summary>Bank-returns R12: the reason for a return of a line the bank cannot send back.</summary>
    public const string UnknownLine = "UnknownLine";

    public static readonly string[] RejectReasons = ["InvalidAccount", "AccountClosed", "InsufficientFunds", "Refused", UnknownLine];

    public static bool IsRejectReason(string? value) => value is not null && RejectReasons.Contains(value, StringComparer.Ordinal);
}
