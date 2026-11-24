using MyWealthV2.Domain.CorporateActions;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// A corporate action of one Instrument of one Tenant (docs/features/corporate-actions.md §4.2, §5): a split, a
/// consolidation or a bonus issue with a ratio and its dates, or a dividend with its terms per share and no ratio
/// (docs/features/dividends.md §4.2). Origin <c>Manual</c> (entered by a TenantAdmin) or
/// <c>Feed</c> (created from an announcement). <c>Announced</c> → <c>Effective</c> → <c>Applied</c>, or
/// <c>Announced</c> → <c>Cancelled</c>; no status goes back (R6). The Instrument and the kind never change (R12, R18).
/// </summary>
public class CorporateAction : BaseAuditableEntity
{
    public const int MemoMaxLength = 200;
    public const int NoteMaxLength = 200;
    public const int ConnectionMaxLength = 50;
    public const int PartyActionIdMaxLength = 100;

    private CorporateAction()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int InstrumentId { get; private set; }

    public CorporateActionKind Kind { get; private set; }

    /// <summary>The ratio; <c>null</c> on a dividend (dividends R2).</summary>
    public int? RatioNew { get; private set; }

    public int? RatioHeld { get; private set; }

    /// <summary>Dividends R3: the terms of a dividend, each <c>null</c> on another kind.</summary>
    public decimal? AmountPerShare { get; private set; }

    public DividendTaxSource? TaxSource { get; private set; }

    public decimal? ImputationCreditPerShare { get; private set; }

    public decimal? ForeignTaxRatePercent { get; private set; }

    public decimal? ReinvestmentPrice { get; private set; }

    /// <summary>Dividends R24, R26: the statement line that paid the dividend into the client money account, and its
    /// amount; at most one, never undone.</summary>
    public int? ReceiptLineId { get; private set; }

    public decimal? ReceivedAmount { get; private set; }

    /// <summary>Dividends R29: when the dividend became paid; from then its entitlements are posted.</summary>
    public DateTimeOffset? PaidAt { get; private set; }

    public DateOnly ExDate { get; private set; }

    public DateOnly? RecordDate { get; private set; }

    public DateOnly? PaymentDate { get; private set; }

    public CorporateActionOrigin Origin { get; private set; }

    public string? Connection { get; private set; }

    public string? PartyActionId { get; private set; }

    /// <summary>The feed version that last changed it; <c>null</c> for a <c>Manual</c> action.</summary>
    public int? FeedVersion { get; private set; }

    public CorporateActionStatus Status { get; private set; }

    public string? Memo { get; private set; }

    public DateTimeOffset? EffectiveAt { get; private set; }

    /// <summary>The person who applied it now (R20); <c>null</c> for the platform.</summary>
    public int? EffectiveByUserId { get; private set; }

    public DateTimeOffset? AppliedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>The person who cancelled it; <c>null</c> for a feed cancellation.</summary>
    public int? CancelledByUserId { get; private set; }

    public string? CancelNote { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>The ratio of a split, a consolidation or a bonus issue. A dividend has none (dividends R2).</summary>
    public CorporateActionRatio Ratio => Kind == CorporateActionKind.Dividend || RatioNew is null || RatioHeld is null
        ? throw new DomainException("A dividend has no ratio.")
        : new(RatioNew.Value, RatioHeld.Value);

    /// <summary>The terms of a dividend; <c>null</c> on another kind.</summary>
    public DividendTerms? Terms => Kind == CorporateActionKind.Dividend
        ? new DividendTerms(AmountPerShare!.Value, TaxSource!.Value, ImputationCreditPerShare!.Value,
            ForeignTaxRatePercent!.Value, ReinvestmentPrice)
        : null;

    /// <summary>Dividends R11: a <c>Manual</c> dividend, <c>Announced</c>. A plan comes only from the feed (R7).</summary>
    public static CorporateAction EnterDividend(
        int tenantId,
        int instrumentId,
        DividendTerms terms,
        DateOnly exDate,
        DateOnly? recordDate,
        DateOnly? paymentDate,
        string? memo)
    {
        EnsureNoPlan(terms);
        var action = New(tenantId, instrumentId, CorporateActionKind.Dividend, CorporateActionOrigin.Manual);
        action.SetDividendTerms(terms, exDate, recordDate, paymentDate);
        action.Memo = TrimMemo(memo);
        return action;
    }

    /// <summary>Dividends R9: a <c>Feed</c> dividend from version <paramref name="version"/> of the party's action
    /// <paramref name="partyActionId"/> on <paramref name="connection"/>, <c>Announced</c>.</summary>
    public static CorporateAction AnnounceDividend(
        int tenantId,
        int instrumentId,
        string connection,
        string partyActionId,
        int version,
        DividendTerms terms,
        DateOnly exDate,
        DateOnly? recordDate,
        DateOnly? paymentDate)
    {
        var action = NewFeed(tenantId, instrumentId, connection, partyActionId, version, CorporateActionKind.Dividend);
        action.SetDividendTerms(terms, exDate, recordDate, paymentDate);
        return action;
    }

    /// <summary>Dividends R11: a person changes a <c>Manual</c> dividend that is still <c>Announced</c>. The kind and
    /// the Instrument stay.</summary>
    public void ChangeDividend(DividendTerms terms, DateOnly exDate, DateOnly? recordDate, DateOnly? paymentDate, string? memo)
    {
        EnsureManual();
        EnsureAnnounced();
        EnsureDividend();
        EnsureNoPlan(terms);
        var trimmed = TrimMemo(memo);
        SetDividendTerms(terms, exDate, recordDate, paymentDate);
        Memo = trimmed;
    }

    /// <summary>Dividends R9: a later version of the party's action replaces the terms and the dates of a <c>Feed</c>
    /// dividend that is still <c>Announced</c>. A version not above <see cref="FeedVersion"/> changes nothing and
    /// returns <c>false</c>.</summary>
    public bool AmendDividend(int version, DividendTerms terms, DateOnly exDate, DateOnly? recordDate, DateOnly? paymentDate)
    {
        EnsureFeed();
        EnsureAnnounced();
        EnsureDividend();
        if (version <= FeedVersion)
        {
            return false;
        }

        SetDividendTerms(terms, exDate, recordDate, paymentDate);
        FeedVersion = version;
        return true;
    }

    /// <summary>Dividends R24, R26: the dividend takes its one receipt, the statement line
    /// <paramref name="lineId"/> of <paramref name="amount"/>. Only while it waits for its payment.</summary>
    public void TakeReceipt(int lineId, decimal amount)
    {
        EnsureDividend();
        if (Status != CorporateActionStatus.Effective || ReceiptLineId is not null)
        {
            throw new DomainException("Only a dividend that waits for its payment can take a receipt.");
        }

        if (amount <= 0m)
        {
            throw new DomainException("A dividend receipt is above 0.");
        }

        ReceiptLineId = lineId;
        ReceivedAmount = amount;
    }

    /// <summary>R15: a <c>Manual</c> corporate action, <c>Announced</c>.</summary>
    public static CorporateAction Enter(
        int tenantId,
        int instrumentId,
        CorporateActionKind kind,
        CorporateActionRatio ratio,
        DateOnly exDate,
        DateOnly? recordDate,
        DateOnly? paymentDate,
        string? memo)
    {
        var action = New(tenantId, instrumentId, kind, CorporateActionOrigin.Manual);
        action.SetTerms(ratio, exDate, recordDate, paymentDate);
        action.Memo = TrimMemo(memo);
        return action;
    }

    /// <summary>R12: a <c>Feed</c> corporate action from version <paramref name="version"/> of the party's action
    /// <paramref name="partyActionId"/> on <paramref name="connection"/>, <c>Announced</c>.</summary>
    public static CorporateAction Announce(
        int tenantId,
        int instrumentId,
        string connection,
        string partyActionId,
        int version,
        CorporateActionKind kind,
        CorporateActionRatio ratio,
        DateOnly exDate,
        DateOnly? recordDate,
        DateOnly? paymentDate)
    {
        var action = NewFeed(tenantId, instrumentId, connection, partyActionId, version, kind);
        action.SetTerms(ratio, exDate, recordDate, paymentDate);
        return action;
    }

    /// <summary>R18: a person changes a <c>Manual</c> action that is still <c>Announced</c>. The Instrument stays.</summary>
    public void Change(
        CorporateActionKind kind,
        CorporateActionRatio ratio,
        DateOnly exDate,
        DateOnly? recordDate,
        DateOnly? paymentDate,
        string? memo)
    {
        EnsureManual();
        EnsureAnnounced();
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException("Unknown corporate action kind.");
        }

        if (Kind == CorporateActionKind.Dividend || kind == CorporateActionKind.Dividend)
        {
            throw new DomainException("The kind of a dividend never changes, and a dividend has no ratio.");
        }

        var kindBefore = Kind;
        Kind = kind;
        try
        {
            SetTerms(ratio, exDate, recordDate, paymentDate);
        }
        catch
        {
            Kind = kindBefore;
            throw;
        }

        Memo = TrimMemo(memo);
    }

    /// <summary>R12: a later version of the party's action replaces the ratio and the dates of a <c>Feed</c> action
    /// that is still <c>Announced</c>. A version not above <see cref="FeedVersion"/> changes nothing and returns
    /// <c>false</c>.</summary>
    public bool Amend(int version, CorporateActionRatio ratio, DateOnly exDate, DateOnly? recordDate, DateOnly? paymentDate)
    {
        EnsureFeed();
        EnsureAnnounced();
        if (version <= FeedVersion)
        {
            return false;
        }

        SetTerms(ratio, exDate, recordDate, paymentDate);
        FeedVersion = version;
        return true;
    }

    /// <summary>R19: a person cancels a <c>Manual</c> action that is still <c>Announced</c>, with a note.</summary>
    public void Cancel(int byUserId, string note, DateTimeOffset at)
    {
        EnsureManual();
        EnsureAnnounced();
        var trimmed = note?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NoteMaxLength)
        {
            throw new DomainException($"A cancel note is 1 to {NoteMaxLength} characters.");
        }

        Status = CorporateActionStatus.Cancelled;
        CancelledAt = at.ToUniversalTime();
        CancelledByUserId = byUserId;
        CancelNote = trimmed;
    }

    /// <summary>R12: the party cancelled its action in version <paramref name="version"/>. No person, no note.</summary>
    public bool CancelFromFeed(int version, DateTimeOffset at)
    {
        EnsureFeed();
        EnsureAnnounced();
        if (version <= FeedVersion)
        {
            return false;
        }

        Status = CorporateActionStatus.Cancelled;
        CancelledAt = at.ToUniversalTime();
        FeedVersion = version;
        return true;
    }

    /// <summary>R22: the action takes effect at <paramref name="at"/>, by <paramref name="byUserId"/> (R20) or by the
    /// platform (<c>null</c>). The caller stores the entitlements in the same database transaction.</summary>
    public void MakeEffective(DateTimeOffset at, int? byUserId = null)
    {
        EnsureAnnounced();
        if (byUserId is not null && Origin != CorporateActionOrigin.Manual)
        {
            throw new DomainException("Only a hand-entered corporate action can be applied by a person.");
        }

        Status = CorporateActionStatus.Effective;
        EffectiveAt = at.ToUniversalTime();
        EffectiveByUserId = byUserId;
    }

    /// <summary>R24: no entitlement is <c>Pending</c> any more.</summary>
    public void MarkApplied(DateTimeOffset at)
    {
        if (Status != CorporateActionStatus.Effective)
        {
            throw new DomainException($"A corporate action that is {Status} cannot be marked applied.");
        }

        Status = CorporateActionStatus.Applied;
        AppliedAt = at.ToUniversalTime();
    }

    private static CorporateAction New(int tenantId, int instrumentId, CorporateActionKind kind, CorporateActionOrigin origin)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException("Unknown corporate action kind.");
        }

        return new CorporateAction
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            InstrumentId = instrumentId,
            Kind = kind,
            Origin = origin,
            Status = CorporateActionStatus.Announced
        };
    }

    private static CorporateAction NewFeed(
        int tenantId, int instrumentId, string connection, string partyActionId, int version, CorporateActionKind kind)
    {
        if (string.IsNullOrWhiteSpace(connection) || connection.Length > ConnectionMaxLength)
        {
            throw new DomainException("A feed corporate action needs its connection.");
        }

        if (string.IsNullOrWhiteSpace(partyActionId) || partyActionId.Length > PartyActionIdMaxLength)
        {
            throw new DomainException("A feed corporate action needs the party's action id.");
        }

        if (version < 1)
        {
            throw new DomainException("A feed version is 1 or more.");
        }

        var action = New(tenantId, instrumentId, kind, CorporateActionOrigin.Feed);
        action.Connection = connection;
        action.PartyActionId = partyActionId;
        action.FeedVersion = version;
        return action;
    }

    private void SetDividendTerms(DividendTerms terms, DateOnly exDate, DateOnly? recordDate, DateOnly? paymentDate)
    {
        if (DividendTerms.RefusalOf(terms.AmountPerShare, terms.TaxSource, terms.ImputationCreditPerShare,
                terms.ForeignTaxRatePercent, terms.ReinvestmentPrice) is { } refusal)
        {
            throw new DomainException(refusal.Message);
        }

        AmountPerShare = terms.AmountPerShare;
        TaxSource = terms.TaxSource;
        ImputationCreditPerShare = terms.ImputationCreditPerShare;
        ForeignTaxRatePercent = terms.ForeignTaxRatePercent;
        ReinvestmentPrice = terms.ReinvestmentPrice;
        ExDate = exDate;
        RecordDate = recordDate;
        PaymentDate = paymentDate;
    }

    private static void EnsureNoPlan(DividendTerms terms)
    {
        if (terms.ReinvestmentPrice is not null)
        {
            throw new DomainException("Reinvestment is available only for dividends that come from a market data connection.");
        }
    }

    private void EnsureDividend()
    {
        if (Kind != CorporateActionKind.Dividend)
        {
            throw new DomainException("Only a dividend has dividend terms.");
        }
    }

    private void SetTerms(CorporateActionRatio ratio, DateOnly exDate, DateOnly? recordDate, DateOnly? paymentDate)
    {
        if (!ratio.IsValidFor(Kind))
        {
            throw new DomainException($"A ratio of {ratio.Text} does not fit a {Kind}.");
        }

        RatioNew = ratio.New;
        RatioHeld = ratio.Held;
        ExDate = exDate;
        RecordDate = recordDate;
        PaymentDate = paymentDate;
    }

    private static string? TrimMemo(string? memo)
    {
        if (memo is null)
        {
            return null;
        }

        var trimmed = memo.Trim();
        if (trimmed.Length is 0 or > MemoMaxLength)
        {
            throw new DomainException($"A memo is 1 to {MemoMaxLength} characters.");
        }

        return trimmed;
    }

    private void EnsureAnnounced()
    {
        if (Status != CorporateActionStatus.Announced)
        {
            throw new DomainException($"This corporate action is {Status} and can no longer change.");
        }
    }

    private void EnsureManual()
    {
        if (Origin != CorporateActionOrigin.Manual)
        {
            throw new DomainException("This corporate action comes from a market data connection and cannot be changed by hand.");
        }
    }

    private void EnsureFeed()
    {
        if (Origin != CorporateActionOrigin.Feed)
        {
            throw new DomainException("Only a feed corporate action takes a feed version.");
        }
    }
}
