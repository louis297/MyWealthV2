namespace MyWealthV2.Domain.Enums;

/// <summary>Which schedule limit set the fee instead of the banded amount (trade-fees R8, R32).</summary>
public enum FeeLimit
{
    Minimum = 0,
    Cap = 1
}
