using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>Corporate-actions R22: a corporate action takes effect. Hand entry (R20), the apply step (R23, R24) and
/// the feed processing (R12) all make an action effective here.</summary>
internal static class CorporateActionEffect
{
    /// <summary>
    /// The action becomes <c>Effective</c> at <paramref name="at"/> and one <c>Pending</c> entitlement is stored for
    /// every Custodial account of its Tenant that holds the instrument now (a holding with quantity above 0). External
    /// accounts get none, and the set never grows. The caller runs this in one database transaction with the save.
    /// </summary>
    public static async Task MakeEffectiveAsync(
        IApplicationDbContext db,
        CorporateAction action,
        DateTimeOffset at,
        int? byUserId,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
