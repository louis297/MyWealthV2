using MyWealthV2.Domain.CorporateActions;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// A corporate action of one Instrument of one Tenant (docs/features/corporate-actions.md §4.2, §5): a split, a
/// consolidation or a bonus issue with a ratio and its dates. Origin <c>Manual</c> (entered by a TenantAdmin) or
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

    public int RatioNew { get; private set; }

    public int RatioHeld { get; private set; }

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

    public CorporateActionRatio Ratio => new(RatioNew, RatioHeld);

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
        action.SetTerms(ratio, exDate, recordDate, paymentDate);
        action.Connection = connection;
        action.PartyActionId = partyActionId;
        action.FeedVersion = version;
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
