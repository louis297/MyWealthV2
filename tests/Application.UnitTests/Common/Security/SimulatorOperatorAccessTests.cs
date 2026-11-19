using System.Reflection;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Common.Security;

/// <summary>Simulator spec R2, R7: the operator holds no permission and signs in only on <c>simulator</c>.</summary>
public class SimulatorOperatorAccessTests
{
    private static IEnumerable<string> AllPolicies() =>
        typeof(Policies)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    [TestCaseSource(nameof(AllPolicies))]
    public void RolePermissions_GrantsTheOperatorNothing(string policy)
    {
        RolePermissions.Has(UserRole.SimulatorOperator, policy).ShouldBeFalse();
    }

    [TestCase(UserRole.SystemAdmin, false)]
    [TestCase(UserRole.TenantAdmin, false)]
    [TestCase(UserRole.Adviser, false)]
    [TestCase(UserRole.Customer, false)]
    [TestCase(UserRole.SimulatorOperator, true)]
    public void SimulatorClient_AllowsTheOperatorOnly(UserRole role, bool expected)
    {
        ClientRoleAllowList.Allows("simulator", role).ShouldBe(expected);
    }

    [TestCase("adviser-portal")]
    [TestCase("customer-portal")]
    [TestCase("back-office")]
    public void OtherClients_DenyTheOperator(string clientId)
    {
        ClientRoleAllowList.Allows(clientId, UserRole.SimulatorOperator).ShouldBeFalse();
    }
}
