namespace MyWealthV2.Application.CorporateActions;

/// <summary>The terms a create or update body carries: a ratio, or the terms of a dividend (dividends R2, R3, R11).</summary>
public interface ICorporateActionTermsBody
{
    string? Kind { get; }

    int? RatioNew { get; }

    int? RatioHeld { get; }

    decimal? AmountPerShare { get; }

    string? TaxSource { get; }

    decimal? ImputationCreditPerShare { get; }

    decimal? ForeignTaxRatePercent { get; }

    decimal? ReinvestmentPrice { get; }
}

/// <summary>
/// Corporate-actions R3 and dividends R2, R3, R7, R11: a dividend's body has terms and no ratio, and no plan price by
/// hand; any other body has a ratio and no dividend term. Each refusal is a 400 at its field.
/// </summary>
internal sealed class CorporateActionTermsValidator : AbstractValidator<ICorporateActionTermsBody>
{
    public CorporateActionTermsValidator()
    {
        When(CorporateActionTerms.IsDividendBody, () =>
        {
            RuleFor(body => body.RatioNew).Null().WithMessage(CorporateActionTerms.NoRatioMessage);
            RuleFor(body => body.RatioHeld).Null().WithMessage(CorporateActionTerms.NoRatioMessage)
                .OverridePropertyName(nameof(ICorporateActionTermsBody.RatioNew));
            RuleFor(body => body.ReinvestmentPrice).Null().WithMessage(CorporateActionTerms.PlanMessage);
            RuleFor(body => body).Custom((body, context) =>
            {
                if (CorporateActionTerms.DividendRefusal(body) is { } refusal)
                {
                    context.AddFailure(refusal.Field, refusal.Message);
                }
            });
        }).Otherwise(() =>
        {
            RuleFor(body => body.RatioNew).NotNull();
            RuleFor(body => body.RatioHeld).NotNull().WithName(nameof(ICorporateActionTermsBody.RatioNew))
                .OverridePropertyName(nameof(ICorporateActionTermsBody.RatioNew));
            RuleFor(body => body.AmountPerShare).Null().WithMessage(OnlyOnADividend);
            RuleFor(body => body.TaxSource).Null().WithMessage(OnlyOnADividend);
            RuleFor(body => body.ImputationCreditPerShare).Null().WithMessage(OnlyOnADividend);
            RuleFor(body => body.ForeignTaxRatePercent).Null().WithMessage(OnlyOnADividend);
            RuleFor(body => body.ReinvestmentPrice).Null().WithMessage(OnlyOnADividend);
        });
    }

    private const string OnlyOnADividend = "{PropertyName} is set only on a dividend.";
}
