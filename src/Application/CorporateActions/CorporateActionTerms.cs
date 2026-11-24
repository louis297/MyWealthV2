using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.CorporateActions;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>The rules hand entry and update share (corporate-actions R3, R15, R17, R18).</summary>
internal static class CorporateActionTerms
{
    public static readonly string[] TaxSourceNames = Enum.GetNames<DividendTaxSource>();

    public const string NoRatioMessage = "A dividend has no ratio.";

    public const string PlanMessage = "Reinvestment is available only for dividends that come from a market data connection.";

    /// <summary>Dividends R2, R11: a body is a dividend's when it names the kind <c>Dividend</c>, or, on an update that
    /// leaves the kind out, when it has no ratio.</summary>
    public static bool IsDividendBody(ICorporateActionTermsBody body) =>
        body.Kind is null
            ? body.RatioNew is null && body.RatioHeld is null
            : string.Equals(body.Kind.Trim(), nameof(CorporateActionKind.Dividend), StringComparison.OrdinalIgnoreCase);

    /// <summary>Dividends R3: the tax source by name; <c>null</c> for a missing or unknown name.</summary>
    public static DividendTaxSource? ParseTaxSource(string? name) =>
        name is not null && TaxSourceNames.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase)
            ? Enum.Parse<DividendTaxSource>(name.Trim(), ignoreCase: true)
            : null;

    /// <summary>Dividends R3: the field and message of the first term that breaks the rule, with no plan price.</summary>
    public static (string Field, string Message)? DividendRefusal(ICorporateActionTermsBody body) =>
        DividendTerms.RefusalOf(
            body.AmountPerShare, ParseTaxSource(body.TaxSource), body.ImputationCreditPerShare, body.ForeignTaxRatePercent, null);

    /// <summary>Dividends R3, R11: the terms of a hand-entered dividend, already validated.</summary>
    public static DividendTerms Dividend(ICorporateActionTermsBody body) =>
        DividendTerms.Create(
            body.AmountPerShare!.Value, ParseTaxSource(body.TaxSource)!.Value, body.ImputationCreditPerShare,
            body.ForeignTaxRatePercent, null);

    /// <summary>R15: the furthest ex-date ahead of the platform's UTC date.</summary>
    public const int MaxDaysAhead = 366;

    public static readonly string[] KindNames = Enum.GetNames<CorporateActionKind>();

    public static bool IsKind(string? kind) => KindNames.Contains(kind, StringComparer.OrdinalIgnoreCase);

    public static CorporateActionKind ParseKind(string kind) => Enum.Parse<CorporateActionKind>(kind, ignoreCase: true);

    public static string KindMessage => $"Kind must be one of {string.Join(", ", KindNames)}.";

    public static bool IsMemo(string? memo) => memo is null || memo.Trim().Length is >= 1 and <= CorporateAction.MemoMaxLength;

    public const string MemoMessage = "Memo must be 1 to 200 characters.";

    /// <summary>R3: the ratio fits the kind; else 400 at <c>RatioNew</c>.</summary>
    public static CorporateActionRatio Ratio(CorporateActionKind kind, int ratioNew, int ratioHeld)
    {
        var ratio = new CorporateActionRatio(ratioNew, ratioHeld);
        if (ratio.IsValidFor(kind))
        {
            return ratio;
        }

        var rule = kind switch
        {
            CorporateActionKind.Split => " A split has more new shares than held.",
            CorporateActionKind.Consolidation => " A consolidation has fewer new shares than held.",
            _ => string.Empty
        };
        throw CorporateActionReader.Error(
            "RatioNew", $"The ratio is two whole numbers from {CorporateActionRatio.Min} to {CorporateActionRatio.Max}.{rule}");
    }

    /// <summary>R15: the ex-date is at most 366 days after the platform's UTC date.</summary>
    public static void EnsureExDate(DateOnly exDate, TimeProvider time)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (exDate > today.AddDays(MaxDaysAhead))
        {
            throw CorporateActionReader.Error("ExDate", $"The ex-date can be at most {MaxDaysAhead} days ahead.");
        }
    }

    /// <summary>R17: no other live <c>Manual</c> action of the instrument with the same kind and ex-date.</summary>
    public static async Task EnsureNoDuplicateAsync(
        IApplicationDbContext db,
        Instrument instrument,
        CorporateActionKind kind,
        DateOnly exDate,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        var exists = await db.CorporateActions.AsNoTracking().AnyAsync(action =>
            action.InstrumentId == instrument.Id
            && action.Origin == CorporateActionOrigin.Manual
            && action.Status != CorporateActionStatus.Cancelled
            && action.Kind == kind
            && action.ExDate == exDate
            && (exceptId == null || action.Id != exceptId), cancellationToken);
        if (exists)
        {
            throw CorporateActionReader.Error(
                "ExDate", $"A {kind.ToString().ToLowerInvariant()} of {instrument.Symbol} with this ex-date already exists.");
        }
    }
}
