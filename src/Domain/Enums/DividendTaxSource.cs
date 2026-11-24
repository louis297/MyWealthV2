namespace MyWealthV2.Domain.Enums;

/// <summary>Whether the company that pays a dividend is resident in New Zealand (dividends R3).</summary>
public enum DividendTaxSource
{
    /// <summary>A New Zealand company: it may attach imputation credits, and an overseas Customer pays NRWT.</summary>
    NewZealand = 0,

    /// <summary>A company not resident in New Zealand: its country may have withheld tax at the source.</summary>
    Foreign = 1
}
