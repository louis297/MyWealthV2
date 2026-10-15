using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Reports;

public static class AdviserBalancesBinder
{
    public const string Code = "adviser-balances";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static AdviserBalancesBinding Bind(JsonElement parameters, UserRole role, Guid? callerPublicId)
    {
        var input = parameters.Deserialize<AdviserBalancesInput>(Options)
                    ?? throw new NotImplementedException();

        Guid adviserId;
        if (input.AdviserId is null || input.AdviserId == Guid.Empty)
        {
            if (role == UserRole.Adviser && callerPublicId is Guid self && self != Guid.Empty)
            {
                adviserId = self;
            }
            else
            {
                throw new NotImplementedException();
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

    private sealed record AdviserBalancesSnapshot(Guid AdviserId, bool IncludeClosedAccounts);

    private sealed class AdviserBalancesInput
    {
        public Guid? AdviserId { get; set; }

        public bool? IncludeClosedAccounts { get; set; }
    }
}
