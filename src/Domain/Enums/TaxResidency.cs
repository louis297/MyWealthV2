namespace MyWealthV2.Domain.Enums;

/// <summary>A Customer's tax residency on their tax profile (interest-run R2). It decides the withholding tax of R4 and nothing else.</summary>
public enum TaxResidency
{
    NewZealand = 0,
    Overseas = 1
}
