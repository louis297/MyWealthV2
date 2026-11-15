using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Fees.TradeSchedules.Queries;

/// <summary>
/// Trade fees R30–R33: compute the fee for up to 5 amounts under a schedule that is not saved. Reads no schedule row,
/// no trade and no cut-off date; writes nothing.
/// </summary>
[Authorize(Policy = Policies.FeesRead)]
public class PreviewTradeFeesQuery : IRequest<TradeFeePreviewDto>
{
    /// <summary>Not part of the body for any caller (R30).</summary>
    public Guid? TenantId { get; init; }

    public string? QuoteCurrency { get; init; }

    public decimal? Minimum { get; init; }

    public decimal? Cap { get; init; }

    public List<FeeBandInput>? Bands { get; init; }

    public List<decimal>? Amounts { get; init; }
}

public class PreviewTradeFeesQueryValidator : AbstractValidator<PreviewTradeFeesQuery>
{
    public const int MaxAmounts = 5;

    public PreviewTradeFeesQueryValidator()
    {
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set on a preview.");
        RuleFor(query => query.QuoteCurrency).NotEmpty();
        RuleFor(query => query.Minimum).NotNull();
        RuleFor(query => query.Bands).NotNull();
        RuleFor(query => query.Amounts)
            .NotNull()
            .Must(amounts => amounts is { Count: >= 1 and <= MaxAmounts })
            .WithMessage($"Send 1 to {MaxAmounts} amounts.");
        RuleForEach(query => query.Amounts)
            .Must(amount => amount > 0m && FeeBands.HasAtMostDecimals(amount, 4))
            .WithMessage("Each amount must be above 0 with at most 4 decimals.");
    }
}

public class PreviewTradeFeesQueryHandler(ICurrencyCatalog catalog)
    : IRequestHandler<PreviewTradeFeesQuery, TradeFeePreviewDto>
{
    public Task<TradeFeePreviewDto> Handle(PreviewTradeFeesQuery request, CancellationToken cancellationToken)
    {
        var currency = catalog.TryGet(request.QuoteCurrency!.Trim());
        if (currency is null || !currency.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(PreviewTradeFeesQuery.QuoteCurrency),
                    "Quote currency must be an enabled catalog code.")
            ]);
        }

        var failures = TradeFeeScheduleRules.Check(request.Minimum!.Value, request.Cap, request.Bands, currency.DecimalPlaces);
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var bands = FeeBands.Create(TradeFeeScheduleRules.ToBands(request.Bands!), currency.DecimalPlaces);
        var items = request.Amounts!
            .Select(amount => TradeFeePreviewItemDto.From(
                TradeFeeSchedule.Calculate(amount, request.Minimum.Value, request.Cap, bands, currency.DecimalPlaces)))
            .ToList();
        return Task.FromResult(new TradeFeePreviewDto(currency.Code, items));
    }
}

public sealed record TradeFeePreviewDto(string Currency, IReadOnlyList<TradeFeePreviewItemDto> Items);

public sealed record TradeFeePreviewItemDto(
    decimal Amount,
    IReadOnlyList<TradeFeePreviewBandDto> Bands,
    decimal Banded,
    string? LimitApplied,
    decimal Fee)
{
    public static TradeFeePreviewItemDto From(FeeCalculation calculation) =>
        new(
            calculation.Amount,
            calculation.Bands
                .Select(band => new TradeFeePreviewBandDto(band.From, band.RatePercent, band.Slice, band.Fee))
                .ToList(),
            calculation.Banded,
            calculation.LimitApplied switch
            {
                FeeLimit.Minimum => "minimum",
                FeeLimit.Cap => "cap",
                _ => null
            },
            calculation.Fee);
}

public sealed record TradeFeePreviewBandDto(decimal From, decimal RatePercent, decimal Slice, decimal Fee);
