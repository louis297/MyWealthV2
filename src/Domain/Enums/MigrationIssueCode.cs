namespace MyWealthV2.Domain.Enums;

/// <summary>Why a migration batch is not valid (docs/features/migration-batches.md R9), in the order of the rule.</summary>
public enum MigrationIssueCode
{
    /// <summary>The batch has no entry.</summary>
    NoEntries = 0,

    /// <summary>The Account is External.</summary>
    AccountNotCustodial = 1,

    /// <summary>The Account is Closed.</summary>
    AccountNotOpen = 2,

    /// <summary>The Account's Customer is Disabled.</summary>
    CustomerDisabled = 3,

    /// <summary>The Account is not empty (R10).</summary>
    AccountNotEmpty = 4,

    /// <summary>The cash has more decimal places than the Account currency allows.</summary>
    CashPrecision = 5,

    /// <summary>The Instrument is disabled.</summary>
    InstrumentInactive = 6,

    /// <summary>The Instrument's quote currency is not the Account's currency.</summary>
    InstrumentCurrencyMismatch = 7,

    /// <summary>A control total differs from the computed total.</summary>
    ControlTotalMismatch = 8,

    /// <summary>The funding of a currency differs from the cash of that currency.</summary>
    FundingMismatch = 9
}
