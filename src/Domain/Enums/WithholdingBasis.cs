namespace MyWealthV2.Domain.Enums;

/// <summary>Why a withholding rate applies (interest-run R4). Shown on the tax profile; not stored.</summary>
public enum WithholdingBasis
{
    /// <summary>The rate the Customer chose.</summary>
    Elected = 0,

    /// <summary>The IRD number is held and no rate was chosen.</summary>
    Default = 1,

    /// <summary>The IRD number is not held.</summary>
    NonDeclaration = 2,

    /// <summary>An overseas Customer.</summary>
    NonResident = 3
}
