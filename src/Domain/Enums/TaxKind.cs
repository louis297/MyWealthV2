namespace MyWealthV2.Domain.Enums;

/// <summary>The kind of a withholding tax on a <c>Tax</c> cash leg (interest-run R25). Later kinds are added by their own specs.</summary>
public enum TaxKind
{
    /// <summary>Resident withholding tax.</summary>
    Rwt = 0,

    /// <summary>Non-resident withholding tax.</summary>
    Nrwt = 1,

    /// <summary>Tax the company's country took from a dividend before it reached the custodian (dividends R38).</summary>
    ForeignWithholding = 2
}
