using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Accounts;

internal static class AccountScope
{
    public static int? AssignedAdviserInternalId(ICurrentUser currentUser) =>
        currentUser.Role == UserRole.Adviser ? currentUser.Person?.Id : null;
}
