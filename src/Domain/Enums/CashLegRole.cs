namespace MyWealthV2.Domain.Enums;

/// <summary>What a cash leg is for (trade-fees R14, R18). Later roles (for example <c>Tax</c>) are added by their own specs.</summary>
public enum CashLegRole
{
    Principal = 0,
    Fee = 1
}
