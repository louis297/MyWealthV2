using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
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

    public static AdviserBalancesBinding Bind(JsonElement parameters, UserRole role, Guid? callerPublicId)
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
        var snapshot = JsonSerializer.Serialize(
            new AdviserBalancesSnapshot(adviserId, includeClosedAccounts),
            Options);
        return new AdviserBalancesBinding(adviserId, includeClosedAccounts, snapshot);
    }

    private static ValidationException Unknown() =>
        new([new ValidationFailure("parameters", "unknown field.")]);

    private sealed record AdviserBalancesSnapshot(Guid AdviserId, bool IncludeClosedAccounts);

    private sealed class AdviserBalancesInput
    {
        public Guid? AdviserId { get; set; }

        public bool? IncludeClosedAccounts { get; set; }
    }
}
