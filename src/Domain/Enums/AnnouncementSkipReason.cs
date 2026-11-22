namespace MyWealthV2.Domain.Enums;

/// <summary>Why an announcement changed no corporate action (corporate-actions R11).</summary>
public enum AnnouncementSkipReason
{
    /// <summary>The adapter did not know its kind.</summary>
    UnsupportedKind = 0,

    /// <summary>The ratio breaks R3, or the connection does not serve the symbol.</summary>
    Invalid = 1,

    /// <summary>No Tenant has an Instrument with that symbol and quote currency.</summary>
    NoInstrument = 2,

    /// <summary>An action it would change is <c>Effective</c>, <c>Applied</c> or <c>Cancelled</c>.</summary>
    AlreadyEffective = 3,

    /// <summary>Every action already has this version or a later one.</summary>
    Superseded = 4
}
