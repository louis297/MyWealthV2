using FluentValidation.Results;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Fees.TradeSchedules;

/// <summary>R6 and R10 refusals, both reported as <c>errors.EffectiveFrom</c> (trade-fees §8.2).</summary>
internal static class EffectiveFromRules
{
    private const string Field = "EffectiveFrom";

    public static void EnsureAfterCutOff(DateOnly effectiveFrom, DateOnly cutOffDate)
    {
        if (effectiveFrom <= cutOffDate)
        {
            throw new ValidationException([
                new ValidationFailure(Field,
                    $"EffectiveFrom must be after {cutOffDate:yyyy-MM-dd} (UTC): today, or the latest trade in this currency.")
            ]);
        }
    }

    public static ValidationException Duplicate() =>
        new([new ValidationFailure(Field, "A schedule for this currency already starts on this date.")]);
}
