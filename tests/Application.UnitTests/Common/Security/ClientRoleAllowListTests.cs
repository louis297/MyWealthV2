using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Common.Security;

public class ClientRoleAllowListTests
{
    [TestCase(UserRole.SystemAdmin, true)]
    [TestCase(UserRole.TenantAdmin, true)]
    [TestCase(UserRole.Adviser, true)]
    [TestCase(UserRole.Customer, false)]
    public void AdviserPortal_AllowsStaffNotCustomer(UserRole role, bool expected)
    {
        ClientRoleAllowList.Allows("adviser-portal", role).ShouldBe(expected);
    }

    [TestCase(UserRole.SystemAdmin, true)]
    [TestCase(UserRole.TenantAdmin, false)]
    [TestCase(UserRole.Adviser, false)]
    [TestCase(UserRole.Customer, false)]
    public void BackOffice_AllowsSystemAdminOnly(UserRole role, bool expected)
    {
        ClientRoleAllowList.Allows("back-office", role).ShouldBe(expected);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("customer-portal")]
    public void UnknownOrReservedClient_DeniesEveryRole(string? clientId)
    {
        foreach (var role in Enum.GetValues<UserRole>())
        {
            ClientRoleAllowList.Allows(clientId, role).ShouldBeFalse();
        }
    }
}
