using System.Text.Json;
using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;

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
}
