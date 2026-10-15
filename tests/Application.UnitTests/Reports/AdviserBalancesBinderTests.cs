using System.Text.Json;
using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.UnitTests.Reports;

public class AdviserBalancesBinderTests
{
    private static readonly Guid Self = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static readonly Guid Named = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Test]
    public void Bind_AdviserOmitsAdviserId_DefaultsToSelfAndClosedAccountsFalse()
    {
        using var omitted = JsonDocument.Parse("{}");
        var binding = AdviserBalancesBinder.Bind(omitted.RootElement, UserRole.Adviser, Self);

        binding.AdviserId.ShouldBe(Self);
        binding.IncludeClosedAccounts.ShouldBeFalse();
        binding.SnapshotJson.ShouldBe(
            $$"""{"adviserId":"{{Self}}","includeClosedAccounts":false}""");

        using var closed = JsonDocument.Parse("""{"includeClosedAccounts":true}""");
        var withClosed = AdviserBalancesBinder.Bind(closed.RootElement, UserRole.Adviser, Self);

        withClosed.AdviserId.ShouldBe(Self);
        withClosed.IncludeClosedAccounts.ShouldBeTrue();
        withClosed.SnapshotJson.ShouldBe(
            $$"""{"adviserId":"{{Self}}","includeClosedAccounts":true}""");

        using var named = JsonDocument.Parse($$"""{"adviserId":"{{Named}}"}""");
        var explicitId = AdviserBalancesBinder.Bind(named.RootElement, UserRole.Adviser, Self);

        explicitId.AdviserId.ShouldBe(Named);
        explicitId.IncludeClosedAccounts.ShouldBeFalse();
        explicitId.SnapshotJson.ShouldBe(
            $$"""{"adviserId":"{{Named}}","includeClosedAccounts":false}""");
    }

    [Test]
    public void Bind_NonAdviserOmitsAdviserId_Rejects()
    {
        using var omitted = JsonDocument.Parse("{}");

        var tenantAdmin = Should.Throw<ValidationException>(() =>
            AdviserBalancesBinder.Bind(omitted.RootElement, UserRole.TenantAdmin, Self));
        tenantAdmin.Errors["adviserId"].ShouldBe(["adviserId is required."]);

        var systemAdmin = Should.Throw<ValidationException>(() =>
            AdviserBalancesBinder.Bind(omitted.RootElement, UserRole.SystemAdmin, null));
        systemAdmin.Errors["adviserId"].ShouldBe(["adviserId is required."]);
    }

    [Test]
    public void Bind_UnknownField_Rejects()
    {
        using var asOf = JsonDocument.Parse(
            $$"""{"adviserId":"{{Named}}","asOf":"2026-10-16T08:30:00Z"}""");
        var clientAsOf = Should.Throw<ValidationException>(() =>
            AdviserBalancesBinder.Bind(asOf.RootElement, UserRole.TenantAdmin, null));
        clientAsOf.Errors["parameters"].ShouldBe(["unknown field."]);

        using var extra = JsonDocument.Parse(
            $$"""{"adviserId":"{{Named}}","customerId":"{{Self}}"}""");
        var unknown = Should.Throw<ValidationException>(() =>
            AdviserBalancesBinder.Bind(extra.RootElement, UserRole.Adviser, Self));
        unknown.Errors["parameters"].ShouldBe(["unknown field."]);
    }
}
