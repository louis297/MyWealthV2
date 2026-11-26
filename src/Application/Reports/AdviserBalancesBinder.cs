using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.MarketData;
using MyWealthV2.Domain.Enums;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports;

public static class AdviserBalancesBinder
{
    public const string Code = "adviser-balances";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static AdviserBalancesBinding Bind(
        JsonElement parameters, UserRole role, Guid? callerPublicId, AdviserBalancesBindMode mode)
    {
        AdviserBalancesInput input;
        try
        {
            input = parameters.Deserialize<AdviserBalancesInput>(Options)
                    ?? throw Unknown();
        }
        catch (JsonException exception) when (exception.Message.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase))
        {
            throw Unknown();
        }
        catch (JsonException)
        {
            throw new ValidationException([
                new ValidationFailure("parameters", "parameters are not valid.")
            ]);
        }

        Guid adviserId;
        if (input.AdviserId is null || input.AdviserId == Guid.Empty)
        {
            if (role == UserRole.Adviser && callerPublicId is Guid self && self != Guid.Empty)
            {
                adviserId = self;
            }
            else
            {
                throw new ValidationException([
                    new ValidationFailure("adviserId", "adviserId is required.")
                ]);
            }
        }
        else
        {
            adviserId = input.AdviserId.Value;
        }

        var includeClosedAccounts = input.IncludeClosedAccounts ?? false;
        var date = BindDate(input, mode);
        var snapshot = JsonSerializer.Serialize(
            new AdviserBalancesSnapshot(adviserId, includeClosedAccounts, date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Options);
        return new AdviserBalancesBinding(adviserId, includeClosedAccounts, snapshot, date);
    }

    /// <summary>Valuation-history R16, R20: the optional end of a past day. A request may name a day up to the last
    /// final day of its moment and not include closed accounts with it; a schedule names none; a stored snapshot is
    /// bound as it was stored.</summary>
    private static DateOnly? BindDate(AdviserBalancesInput input, AdviserBalancesBindMode mode)
    {
        if (input.Date is null)
        {
            return null;
        }

        if (mode.Kind == AdviserBalancesBindKind.Schedule)
        {
            throw Invalid("date", "A schedule reports the balances at the time it runs.");
        }

        if (MarketDataDates.Parse(input.Date) is not { } date)
        {
            throw Invalid("date", $"date {MarketDataDates.FormatMessage}");
        }

        if (mode.Kind == AdviserBalancesBindKind.Request)
        {
            if (date > mode.LastFinalDay)
            {
                throw Invalid("date", "A report for a past date can be made up to the day before yesterday (UTC).");
            }

            if (input.IncludeClosedAccounts == true)
            {
                throw Invalid(
                    "includeClosedAccounts",
                    "A report for a past date includes every account that held cash or holdings on that day.");
            }
        }

        return date;
    }

    private static ValidationException Invalid(string key, string message) => new([new ValidationFailure(key, message)]);

    private static ValidationException Unknown() =>
        new([new ValidationFailure("parameters", "unknown field.")]);

    private sealed record AdviserBalancesSnapshot(
        Guid AdviserId,
        bool IncludeClosedAccounts,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Date);

    private sealed class AdviserBalancesInput
    {
        public Guid? AdviserId { get; set; }

        public bool? IncludeClosedAccounts { get; set; }

        public string? Date { get; set; }
    }
}
