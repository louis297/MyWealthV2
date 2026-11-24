namespace MyWealthV2.Domain.Entities;

/// <summary>
/// One version of one corporate action at an outside party, as read through the corporate action feed
/// (docs/features/corporate-actions.md R7, R9–R11; ADR 0021 rule 7). Platform data with no Tenant, stored once per
/// (connection, outside id), then processed into Tenant corporate actions or skipped with a reason.
/// </summary>
public class CorporateActionAnnouncement
{
    public const int ConnectionMaxLength = 50;
    public const int IdMaxLength = 100;
    public const int SymbolMaxLength = 32;

    private CorporateActionAnnouncement()
    {
    }

    public int Id { get; private set; }

    public Guid PublicId { get; private set; }

    public string Connection { get; private set; } = string.Empty;

    public string OutsideId { get; private set; } = string.Empty;

    public string PartyActionId { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public PartyActionStatus PartyStatus { get; private set; }

    public string Symbol { get; private set; } = string.Empty;

    public string Currency { get; private set; } = string.Empty;

    /// <summary>The kind; <c>null</c> for a kind the adapter did not know (R9).</summary>
    public CorporateActionKind? Kind { get; private set; }

    /// <summary>The ratio; <c>null</c> on a dividend (dividends R8).</summary>
    public int? RatioNew { get; private set; }

    public int? RatioHeld { get; private set; }

    /// <summary>Dividends R8: the terms of a dividend, each <c>null</c> on another kind.</summary>
    public decimal? AmountPerShare { get; private set; }

    public DividendTaxSource? TaxSource { get; private set; }

    public decimal? ImputationCreditPerShare { get; private set; }

    public decimal? ForeignTaxRatePercent { get; private set; }

    public decimal? ReinvestmentPrice { get; private set; }

    public DateOnly ExDate { get; private set; }

    public DateOnly? RecordDate { get; private set; }

    public DateOnly? PaymentDate { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public AnnouncementStatus Status { get; private set; }

    public AnnouncementSkipReason? SkipReason { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>R9: the fields every item needs before a page can be stored. The kind may be missing.</summary>
    public static bool IsStorable(
        string? outsideId,
        string? partyActionId,
        int? version,
        PartyActionStatus? status,
        string? symbol,
        string? currency,
        int? ratioNew,
        int? ratioHeld,
        DateOnly? exDate,
        CorporateActionKind? kind = null,
        decimal? amountPerShare = null,
        DividendTaxSource? taxSource = null) =>
        outsideId is { Length: >= 1 and <= IdMaxLength }
        && partyActionId is { Length: >= 1 and <= IdMaxLength }
        && version >= 1
        && status is not null && Enum.IsDefined(status.Value)
        && symbol is { Length: >= 1 and <= SymbolMaxLength }
        && currency is { Length: 3 } && currency.All(char.IsAsciiLetter)
        && ratioNew >= 1
        && ratioHeld >= 1
        && exDate is not null;

    /// <summary>R10: a new announcement, <c>Received</c> at platform time.</summary>
    public static CorporateActionAnnouncement Receive(
        string connection,
        string outsideId,
        string partyActionId,
        int version,
        PartyActionStatus partyStatus,
        string symbol,
        string currency,
        CorporateActionKind? kind,
        int? ratioNew,
        int? ratioHeld,
        DateOnly exDate,
        DateOnly? recordDate,
        DateOnly? paymentDate,
        DateTimeOffset receivedAt,
        decimal? amountPerShare = null,
        DividendTaxSource? taxSource = null,
        decimal? imputationCreditPerShare = null,
        decimal? foreignTaxRatePercent = null,
        decimal? reinvestmentPrice = null)
    {
        if (!IsStorable(outsideId, partyActionId, version, partyStatus, symbol, currency, ratioNew, ratioHeld, exDate)
            || string.IsNullOrWhiteSpace(connection) || connection.Length > ConnectionMaxLength)
        {
            throw new DomainException("This corporate action announcement cannot be stored.");
        }

        return new CorporateActionAnnouncement
        {
            PublicId = Guid.NewGuid(),
            Connection = connection,
            OutsideId = outsideId,
            PartyActionId = partyActionId,
            Version = version,
            PartyStatus = partyStatus,
            Symbol = symbol,
            Currency = currency.ToUpperInvariant(),
            Kind = kind is { } known && Enum.IsDefined(known) ? known : null,
            RatioNew = ratioNew,
            RatioHeld = ratioHeld,
            ExDate = exDate,
            RecordDate = recordDate,
            PaymentDate = paymentDate,
            ReceivedAt = receivedAt.ToUniversalTime(),
            Status = AnnouncementStatus.Received
        };
    }

    /// <summary>R11: it created or changed at least one corporate action.</summary>
    public void MarkProcessed(DateTimeOffset at)
    {
        EnsureReceived();
        Status = AnnouncementStatus.Processed;
        ProcessedAt = at.ToUniversalTime();
    }

    /// <summary>R11: it changed nothing, for <paramref name="reason"/>.</summary>
    public void Skip(AnnouncementSkipReason reason, DateTimeOffset at)
    {
        EnsureReceived();
        Status = AnnouncementStatus.Skipped;
        SkipReason = reason;
        ProcessedAt = at.ToUniversalTime();
    }

    private void EnsureReceived()
    {
        if (Status != AnnouncementStatus.Received)
        {
            throw new DomainException($"This announcement is {Status} already.");
        }
    }
}
