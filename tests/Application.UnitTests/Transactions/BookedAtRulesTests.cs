using FluentValidation;
using MyWealthV2.Application.Transactions;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Transactions;

/// <summary>fixes/ledger-write-guards.md Decision 6: bookedAt is not later than the server clock plus 5 minutes.</summary>
public class BookedAtRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 23, 58, 0, TimeSpan.Zero);

    [Test]
    public void TheClockItself_IsValid() =>
        Validate(Now).IsValid.ShouldBeTrue();

    [Test]
    public void TheClockPlusExactlyFiveMinutes_IsValid() =>
        Validate(Now.AddMinutes(5)).IsValid.ShouldBeTrue();

    [Test]
    public void TheClockPlusFiveMinutesAndOneSecond_IsInvalid()
    {
        var result = Validate(Now.AddMinutes(5).AddSeconds(1));

        result.IsValid.ShouldBeFalse();
        var error = result.Errors.ShouldHaveSingleItem();
        error.PropertyName.ShouldBe("BookedAt");
        error.ErrorMessage.ShouldBe("bookedAt cannot be in the future.");
    }

    [Test]
    public void AnOffsetIsComparedAsAnInstant()
    {
        Validate(Now.ToOffset(TimeSpan.FromHours(13)).AddMinutes(5)).IsValid.ShouldBeTrue();
        Validate(Now.ToOffset(TimeSpan.FromHours(13)).AddMinutes(6)).IsValid.ShouldBeFalse();
    }

    [Test]
    public void YearsInThePast_IsValid() =>
        Validate(Now.AddYears(-6)).IsValid.ShouldBeTrue();

    [Test]
    public void NoBookedAt_IsLeftToTheRequiredRule() =>
        Validate(null).IsValid.ShouldBeTrue();

    private static FluentValidation.Results.ValidationResult Validate(DateTimeOffset? bookedAt) =>
        new ProbeValidator(new FixedTime(Now)).Validate(new Probe { BookedAt = bookedAt });

    private sealed class Probe
    {
        public DateTimeOffset? BookedAt { get; init; }
    }

    private sealed class ProbeValidator : AbstractValidator<Probe>
    {
        public ProbeValidator(TimeProvider time) =>
            RuleFor(probe => probe.BookedAt).NotInTheFuture(time);
    }

    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
