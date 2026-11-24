using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>
/// Dividends R29: a dividend becomes paid in the database transaction that completes the last of its conditions. The
/// bank job, the hand match, and later the custodian job and pay in cash, each call this after their own change. The
/// apply step then posts its entitlements (R36).
/// </summary>
internal static class PayDividend
{
    public static bool Try(CorporateAction action, DateTimeOffset at) => action.TryPay(at);
}
