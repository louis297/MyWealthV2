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

    /// <summary>A request on Friday 13 March 2026: the last final day is Wednesday 11.</summary>
    private static readonly AdviserBalancesBindMode Request = AdviserBalancesBindMode.Request(new DateOnly(2026, 3, 11));

    [Test]
    public void Bind_AdviserOmitsAdviserId_DefaultsToSelfAndClosedAccountsFalse()
    {
        using var omitted = JsonDocument.Parse("{}");
        var binding = AdviserBalancesBinder.Bind(omitted.RootElement, UserRole.Adviser, Self, Request);

        binding.AdviserId.ShouldBe(Self);
        binding.IncludeClosedAccounts.ShouldBeFalse();
        binding.SnapshotJson.ShouldBe(
            $$"""{"adviserId":"{{Self}}","includeClosedAccounts":false}""");

        using var closed = JsonDocument.Parse("""{"includeClosedAccounts":true}""");
        var withClosed = AdviserBalancesBinder.Bind(closed.RootElement, UserRole.Adviser, Self, Request);

        withClosed.AdviserId.ShouldBe(Self);
        withClosed.IncludeClosedAccounts.ShouldBeTrue();
        withClosed.SnapshotJson.ShouldBe(
            $$"""{"adviserId":"{{Self}}","includeClosedAccounts":true}""");

        using var named = JsonDocument.Parse($$"""{"adviserId":"{{Named}}"}""");
        var explicitId = AdviserBalancesBinder.Bind(named.RootElement, UserRole.Adviser, Self, Request);

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
            AdviserBalancesBinder.Bind(omitted.RootElement, UserRole.TenantAdmin, Self, Request));
        tenantAdmin.Errors["adviserId"].ShouldBe(["adviserId is required."]);

        var systemAdmin = Should.Throw<ValidationException>(() =>
            AdviserBalancesBinder.Bind(omitted.RootElement, UserRole.SystemAdmin, null, Request));
        systemAdmin.Errors["adviserId"].ShouldBe(["adviserId is required."]);
    }

    [Test]
    public void Bind_UnknownField_Rejects()
    {
        using var asOf = JsonDocument.Parse(
            $$"""{"adviserId":"{{Named}}","asOf":"2026-10-16T08:30:00Z"}""");
        var clientAsOf = Should.Throw<ValidationException>(() =>
            AdviserBalancesBinder.Bind(asOf.RootElement, UserRole.TenantAdmin, null, Request));
        clientAsOf.Errors["parameters"].ShouldBe(["unknown field."]);

        using var extra = JsonDocument.Parse(
            $$"""{"adviserId":"{{Named}}","customerId":"{{Self}}"}""");
        var unknown = Should.Throw<ValidationException>(() =>
            AdviserBalancesBinder.Bind(extra.RootElement, UserRole.Adviser, Self, Request));
        unknown.Errors["parameters"].ShouldBe(["unknown field."]);
    }

    [Test]
    public void Bind_ARequestWithADate_KeepsItInTheSnapshot()
    {
        using var dated = JsonDocument.Parse("""{"date":"2026-03-11"}""");

        var binding = AdviserBalancesBinder.Bind(dated.RootElement, UserRole.Adviser, Self, Request);

        binding.Date.ShouldBe(new DateOnly(2026, 3, 11));
        binding.IncludeClosedAccounts.ShouldBeFalse();
        binding.SnapshotJson.ShouldBe(
            $$"""{"adviserId":"{{Self}}","includeClosedAccounts":false,"date":"2026-03-11"}""");

        using var withFalse = JsonDocument.Parse("""{"date":"2026-03-02","includeClosedAccounts":false}""");
        AdviserBalancesBinder.Bind(withFalse.RootElement, UserRole.Adviser, Self, Request).Date
            .ShouldBe(new DateOnly(2026, 3, 2));
    }

    [TestCase("2026-3-11", "date must be a date as yyyy-MM-dd.")]
    [TestCase("yesterday", "date must be a date as yyyy-MM-dd.")]
    [TestCase("2026-03-12", "A report for a past date can be made up to the day before yesterday (UTC).")]
    public void Bind_ARequestWithADateThatIsNotAllowed_Rejects(string date, string message)
    {
        using var dated = JsonDocument.Parse($$"""{"date":"{{date}}"}""");

        var error = Should.Throw<ValidationException>(() =>
            AdviserBalancesBinder.Bind(dated.RootElement, UserRole.Adviser, Self, Request));

        error.Errors.Keys.ShouldBe(["date"]);
        error.Errors["date"].ShouldBe([message]);
    }

    [Test]
    public void Bind_ADateWithClosedAccounts_Rejects()
    {
        using var dated = JsonDocument.Parse("""{"date":"2026-03-11","includeClosedAccounts":true}""");

        var error = Should.Throw<ValidationException>(() =>
            AdviserBalancesBinder.Bind(dated.RootElement, UserRole.Adviser, Self, Request));

        error.Errors.Keys.ShouldBe(["includeClosedAccounts"]);
        error.Errors["includeClosedAccounts"].ShouldBe(
            ["A report for a past date includes every account that held cash or holdings on that day."]);
    }

    [Test]
    public void Bind_AScheduleWithADate_Rejects_AndOneWithoutIsBound()
    {
        using var dated = JsonDocument.Parse("""{"date":"2026-03-11"}""");
        using var undated = JsonDocument.Parse("{}");

        var error = Should.Throw<ValidationException>(() =>
            AdviserBalancesBinder.Bind(dated.RootElement, UserRole.Adviser, Self, AdviserBalancesBindMode.Schedule));

        error.Errors.Keys.ShouldBe(["date"]);
        error.Errors["date"].ShouldBe(["A schedule reports the balances at the time it runs."]);
        AdviserBalancesBinder.Bind(undated.RootElement, UserRole.Adviser, Self, AdviserBalancesBindMode.Schedule)
            .Date.ShouldBeNull();
    }

    [Test]
    public void Bind_ASnapshotWithADate_IsBoundAsItIs()
    {
        using var snapshot = JsonDocument.Parse(
            $$"""{"adviserId":"{{Named}}","includeClosedAccounts":false,"date":"2026-03-11"}""");

        var binding = AdviserBalancesBinder.Bind(snapshot.RootElement, UserRole.TenantAdmin, null, AdviserBalancesBindMode.Snapshot);

        (binding.AdviserId, binding.Date).ShouldBe((Named, new DateOnly(2026, 3, 11)));
        binding.SnapshotJson.ShouldBe(snapshot.RootElement.GetRawText());
    }
}
