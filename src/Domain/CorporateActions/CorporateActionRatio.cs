using System.Globalization;

namespace MyWealthV2.Domain.CorporateActions;

/// <summary>
/// The ratio of a corporate action: two whole numbers read "<see cref="New"/> for <see cref="Held"/>", stored as given
/// and not reduced (corporate-actions R3). <see cref="Apply"/> works out the quantity after the action (R28).
/// </summary>
public sealed record CorporateActionRatio(int New, int Held)
{
    public const int Min = 1;

    public const int Max = 10_000;

    /// <summary>The scale of a quantity (holdings R13).</summary>
    public const int QuantityDecimals = 8;

    /// <summary>Both numbers are 1–10 000 and the ratio fits the kind (R3).</summary>
    public bool IsValidFor(CorporateActionKind kind)
    {
        if (New is < Min or > Max || Held is < Min or > Max)
        {
            return false;
        }

        return kind switch
        {
            CorporateActionKind.Split => New > Held,
            CorporateActionKind.Consolidation => New < Held,
            CorporateActionKind.Bonus => true,
            _ => false
        };
    }

    /// <summary><c>RoundDown(quantity × factor, 8)</c>, the factor worked out as a fraction: multiply first, then divide
    /// (R28). A result of 0 or equal to <paramref name="quantity"/> is returned as is; the caller decides (R25).</summary>
    public decimal Apply(CorporateActionKind kind, decimal quantity)
    {
        if (!IsValidFor(kind))
        {
            throw new DomainException($"A ratio of {Text} does not fit a {kind}.");
        }

        if (quantity < 0)
        {
            throw new DomainException("A corporate action applies to a quantity of 0 or more.");
        }

        var numerator = kind == CorporateActionKind.Bonus ? Held + New : New;
        return Math.Round(quantity * numerator / Held, QuantityDecimals, MidpointRounding.ToZero);
    }

    /// <summary>The ratio as text, for example <c>2 for 1</c>.</summary>
    public string Text => string.Create(CultureInfo.InvariantCulture, $"{New} for {Held}");
}
