using System.Text.Json;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Reports;

public static class AdviserBalancesBinder
{
    public const string Code = "adviser-balances";

    public static AdviserBalancesBinding Bind(JsonElement parameters, UserRole role, Guid? callerPublicId) =>
        throw new NotImplementedException();
}
