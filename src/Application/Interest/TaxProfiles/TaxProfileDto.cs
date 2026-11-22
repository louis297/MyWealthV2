using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.Interest.TaxProfiles;

/// <summary>Interest-run R8: the tax profile item.</summary>
public sealed record TaxProfileDto(
    string TaxResidency,
    bool IrdNumberHeld,
    decimal? RwtRatePercent,
    WithholdingDto Withholding,
    TaxProfileUpdatedByDto? UpdatedBy,
    DateTimeOffset? UpdatedAt,
    string? RowVersion)
{
    /// <summary>The item of a stored profile, or of <see cref="CustomerTaxProfile.NotProvided"/> when
    /// <paramref name="profile"/> is null. <paramref name="updatedBy"/> is null for a Customer (R8).</summary>
    public static TaxProfileDto From(CustomerTaxProfile? profile, TaxProfileUpdatedByDto? updatedBy)
    {
        var shown = profile ?? CustomerTaxProfile.NotProvided;
        var withholding = shown.Withholding;
        return new TaxProfileDto(
            shown.TaxResidency.ToString(),
            shown.IrdNumberHeld,
            shown.RwtRatePercent,
            new WithholdingDto(withholding.Kind.ToString(), withholding.RatePercent, withholding.Basis.ToString()),
            profile is null ? null : updatedBy,
            profile?.UpdatedAt,
            profile is null ? null : Convert.ToBase64String(profile.RowVersion ?? []));
    }
}

public sealed record WithholdingDto(string Kind, decimal RatePercent, string Basis);

public sealed record TaxProfileUpdatedByDto(Guid Id, string Name);
