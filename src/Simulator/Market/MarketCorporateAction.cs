using System.Globalization;

namespace MyWealthV2.Simulator.Market;

/// <summary>The kind of a simulated corporate action (corporate-actions R39); the same names as the platform's.</summary>
public enum MarketCorporateActionKind
{
    Split = 0,
    Consolidation = 1,
    Bonus = 2,

    /// <summary>Cash per share held, with terms in place of a ratio (dividends R54).</summary>
    Dividend = 3
}

/// <summary>Whether the issuer of a dividend is resident in New Zealand (dividends R3, R54).</summary>
public enum MarketDividendTaxSource
{
    NewZealand = 0,
    Foreign = 1
}

/// <summary>The terms of a simulated dividend, per share (dividends R54).</summary>
public sealed record MarketDividendTerms(
    decimal AmountPerShare,
    MarketDividendTaxSource TaxSource,
    decimal ImputationCreditPerShare,
    decimal ForeignTaxRatePercent,
    decimal? ReinvestmentPrice)
{
    /// <summary>R59: the dividend per share net of the foreign tax the custodian keeps.</summary>
    public decimal NetPerShare => AmountPerShare * (1m - ForeignTaxRatePercent / 100m);
}

/// <summary>The issuer's status of a simulated corporate action (R39–R42).</summary>
public enum MarketCorporateActionStatus
{
    Announced = 0,
    Effective = 1,
    Cancelled = 2
}

/// <summary>
/// A corporate action the simulator market announces for one of its instruments (corporate-actions R39–R43). Every
/// announce, amend, cancel and effective appends one version, the feed's items. Its own books: it never learns who
/// holds the instrument (R47).
/// </summary>
public sealed class MarketCorporateAction
{
    public const int MaxRatio = 10_000;

    public int Id { get; private set; }

    /// <summary>R43: the <c>n</c> of the party action id, counting the actions of the world from 1.</summary>
    public int Number { get; private set; }

    public int InstrumentId { get; private set; }

    public MarketCorporateActionKind Kind { get; private set; }

    public int? RatioNew { get; private set; }

    public int? RatioHeld { get; private set; }

    /// <summary>Dividends R54: the terms of a dividend, each <c>null</c> on another kind.</summary>
    public decimal? AmountPerShare { get; private set; }

    public MarketDividendTaxSource? TaxSource { get; private set; }

    public decimal? ImputationCreditPerShare { get; private set; }

    public decimal? ForeignTaxRatePercent { get; private set; }

    public decimal? ReinvestmentPrice { get; private set; }

    /// <summary>Dividends R59: the simulator day the custodian paid the dividend.</summary>
    public DateOnly? PaidOn { get; private set; }

    public MarketDividendTerms? Terms => Kind == MarketCorporateActionKind.Dividend
        ? new MarketDividendTerms(AmountPerShare!.Value, TaxSource!.Value, ImputationCreditPerShare ?? 0m, ForeignTaxRatePercent ?? 0m,
            ReinvestmentPrice)
        : null;

    public DateOnly ExDate { get; private set; }

    public DateOnly? RecordDate { get; private set; }

    public DateOnly? PaymentDate { get; private set; }

    public MarketCorporateActionStatus Status { get; private set; }

    public int Version { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public List<MarketCorporateActionVersion> Versions { get; private set; } = [];

    /// <summary>R43: <c>{WorldId:N}-CA{n}</c>.</summary>
    public static string ActionId(Guid worldId, int number) =>
        $"{worldId:N}-CA{number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>R43: the number of a party action id of <paramref name="worldId"/>, or false.</summary>
    public static bool TryReadNumber(string id, Guid worldId, out int number)
    {
        number = 0;
        var prefix = $"{worldId:N}-CA";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(id[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>R3 as the simulator holds it: two whole numbers 1–10 000 that fit the kind.</summary>
    public static bool FitsKind(MarketCorporateActionKind kind, int? ratioNew, int? ratioHeld) =>
        ratioNew is >= 1 and <= MaxRatio && ratioHeld is >= 1 and <= MaxRatio
        && kind switch
        {
            MarketCorporateActionKind.Split => ratioNew > ratioHeld,
            MarketCorporateActionKind.Consolidation => ratioNew < ratioHeld,
            _ => true
        };

    /// <summary>R39: <c>Announced</c>, version 1.</summary>
    public static MarketCorporateAction Announce(
        int number,
        int instrumentId,
        MarketCorporateActionKind kind,
        int ratioNew,
        int ratioHeld,
        DateOnly exDate,
        DateOnly? recordDate,
        DateOnly? paymentDate)
    {
        var action = new MarketCorporateAction
        {
            Number = number,
            InstrumentId = instrumentId,
            Kind = kind,
            RatioNew = ratioNew,
            RatioHeld = ratioHeld,
            ExDate = exDate,
            RecordDate = recordDate,
            PaymentDate = paymentDate,
            Status = MarketCorporateActionStatus.Announced
        };
        action.AppendVersion();
        return action;
    }

    /// <summary>Dividends R54: a dividend, <c>Announced</c>, version 1.</summary>
    public static MarketCorporateAction AnnounceDividend(
        int number,
        int instrumentId,
        MarketDividendTerms terms,
        DateOnly exDate,
        DateOnly? recordDate,
        DateOnly paymentDate)
    {
        var action = new MarketCorporateAction
        {
            Number = number,
            InstrumentId = instrumentId,
            Kind = MarketCorporateActionKind.Dividend,
            Status = MarketCorporateActionStatus.Announced
        };
        action.SetDividend(terms, exDate, recordDate, paymentDate);
        action.AppendVersion();
        return action;
    }

    /// <summary>Dividends R55: new terms and dates while <c>Announced</c>.</summary>
    public void AmendDividend(MarketDividendTerms terms, DateOnly exDate, DateOnly? recordDate, DateOnly paymentDate)
    {
        EnsureAnnounced();
        SetDividend(terms, exDate, recordDate, paymentDate);
        AppendVersion();
    }

    /// <summary>Dividends R59: the custodian paid the dividend on <paramref name="day"/>.</summary>
    public void MarkPaid(DateOnly day) => PaidOn = day;

    private void SetDividend(MarketDividendTerms terms, DateOnly exDate, DateOnly? recordDate, DateOnly paymentDate)
    {
        AmountPerShare = terms.AmountPerShare;
        TaxSource = terms.TaxSource;
        ImputationCreditPerShare = terms.ImputationCreditPerShare;
        ForeignTaxRatePercent = terms.ForeignTaxRatePercent;
        ReinvestmentPrice = terms.ReinvestmentPrice;
        ExDate = exDate;
        RecordDate = recordDate;
        PaymentDate = paymentDate;
    }

    /// <summary>R40: new terms while <c>Announced</c>; the kind and the instrument stay.</summary>
    public void Amend(int ratioNew, int ratioHeld, DateOnly exDate, DateOnly? recordDate, DateOnly? paymentDate)
    {
        EnsureAnnounced();
        RatioNew = ratioNew;
        RatioHeld = ratioHeld;
        ExDate = exDate;
        RecordDate = recordDate;
        PaymentDate = paymentDate;
        AppendVersion();
    }

    /// <summary>R41: withdrawn while <c>Announced</c>.</summary>
    public void Cancel()
    {
        EnsureAnnounced();
        Status = MarketCorporateActionStatus.Cancelled;
        AppendVersion();
    }

    /// <summary>In-kind-transfers R68: the custodian has applied this effective action to its positions.</summary>
    public bool CustodianApplied { get; private set; }

    public void MarkCustodianApplied() => CustodianApplied = true;

    /// <summary>In-kind-transfers R68: the factor of corporate-actions R3 a holding is multiplied by.</summary>
    public decimal Factor => Kind == MarketCorporateActionKind.Bonus
        ? (decimal)(RatioHeld!.Value + RatioNew!.Value) / RatioHeld.Value
        : (decimal)RatioNew!.Value / RatioHeld!.Value;

    /// <summary>R42: the clock reached the ex-date.</summary>
    public void MakeEffective()
    {
        EnsureAnnounced();
        Status = MarketCorporateActionStatus.Effective;
        AppendVersion();
    }

    /// <summary>R42: the price of the day before, on the new basis (before that day's move).</summary>
    public decimal Rebase(decimal previous) => Kind switch
    {
        // Dividends R56: a dividend leaves the price as it is.
        MarketCorporateActionKind.Dividend => previous,
        MarketCorporateActionKind.Bonus => previous * RatioHeld!.Value / (RatioHeld.Value + RatioNew!.Value),
        _ => previous * RatioHeld!.Value / RatioNew!.Value
    };

    private void EnsureAnnounced()
    {
        if (Status != MarketCorporateActionStatus.Announced)
        {
            throw new InvalidOperationException($"The corporate action is {Status}.");
        }
    }

    private void AppendVersion()
    {
        Version++;
        Versions.Add(new MarketCorporateActionVersion(Version, Status, RatioNew, RatioHeld, ExDate, RecordDate, PaymentDate)
        {
            AmountPerShare = AmountPerShare,
            TaxSource = TaxSource,
            ImputationCreditPerShare = ImputationCreditPerShare,
            ForeignTaxRatePercent = ForeignTaxRatePercent,
            ReinvestmentPrice = ReinvestmentPrice
        });
    }
}

/// <summary>One version of a simulated corporate action: one item of the feed. <see cref="Id"/> is its place.</summary>
public sealed class MarketCorporateActionVersion
{
    private MarketCorporateActionVersion()
    {
    }

    public MarketCorporateActionVersion(
        int version,
        MarketCorporateActionStatus status,
        int? ratioNew,
        int? ratioHeld,
        DateOnly exDate,
        DateOnly? recordDate,
        DateOnly? paymentDate)
    {
        Version = version;
        Status = status;
        RatioNew = ratioNew;
        RatioHeld = ratioHeld;
        ExDate = exDate;
        RecordDate = recordDate;
        PaymentDate = paymentDate;
    }

    public long Id { get; private set; }

    public int ActionId { get; private set; }

    public int Version { get; private set; }

    public MarketCorporateActionStatus Status { get; private set; }

    public int? RatioNew { get; private set; }

    public int? RatioHeld { get; private set; }

    public DateOnly ExDate { get; private set; }

    public DateOnly? RecordDate { get; private set; }

    public DateOnly? PaymentDate { get; private set; }

    /// <summary>Dividends R61: the terms of a dividend's version, each <c>null</c> on another kind.</summary>
    public decimal? AmountPerShare { get; init; }

    public MarketDividendTaxSource? TaxSource { get; init; }

    public decimal? ImputationCreditPerShare { get; init; }

    public decimal? ForeignTaxRatePercent { get; init; }

    public decimal? ReinvestmentPrice { get; init; }
}
