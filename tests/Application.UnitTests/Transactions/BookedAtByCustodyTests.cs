using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Transactions;

/// <summary>Custody modes R10–R12: the server sets bookedAt on a Custodial account; External is unchanged.</summary>
public class BookedAtByCustodyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Past = new(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(13));

    [Test]
    public void Custodial_WithoutBookedAt_ReturnsTheServerClock() =>
        BookedAtByCustody.Resolve(CustodyMode.Custodial, null, new FixedTime(Now)).ShouldBe(Now);

    [Test]
    public void Custodial_WithoutBookedAt_OnTheQuote_ReturnsTheServerClock() =>
        BookedAtByCustody.Resolve(CustodyMode.Custodial, null, new FixedTime(Now), requiredWhenExternal: false)
            .ShouldBe(Now);

    [TestCase(0)]
    [TestCase(-3600)]
    [TestCase(3600)]
    public void Custodial_WithAnyBookedAt_IsRefusedOnBookedAt(int offsetSeconds)
    {
        var exception = Should.Throw<ValidationException>(() =>
            BookedAtByCustody.Resolve(CustodyMode.Custodial, Now.AddSeconds(offsetSeconds), new FixedTime(Now)));

        exception.Errors.ShouldContainKey("BookedAt");
        exception.Errors["BookedAt"].ShouldBe([BookedAtByCustody.CustodialMessage]);
    }

    [Test]
    public void External_WithBookedAt_ReturnsTheCallersInstant() =>
        BookedAtByCustody.Resolve(CustodyMode.External, Past, new FixedTime(Now)).ShouldBe(Past);

    [Test]
    public void External_WithoutBookedAt_IsRefusedOnBookedAt()
    {
        var exception = Should.Throw<ValidationException>(() =>
            BookedAtByCustody.Resolve(CustodyMode.External, null, new FixedTime(Now)));

        exception.Errors.ShouldContainKey("BookedAt");
        exception.Errors["BookedAt"].ShouldBe([BookedAtByCustody.RequiredMessage]);
    }

    [Test]
    public void External_WithoutBookedAt_OnTheQuote_DefaultsToTheServerClock() =>
        BookedAtByCustody.Resolve(CustodyMode.External, null, new FixedTime(Now), requiredWhenExternal: false)
            .ShouldBe(Now);

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
