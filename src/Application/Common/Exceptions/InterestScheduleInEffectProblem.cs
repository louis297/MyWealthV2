namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>
/// The interest rate schedule row may already apply to an accrued day, so it cannot be updated or deleted
/// (api-design §4.2 <c>interest_schedule_in_effect</c>; interest-run R13).
/// </summary>
public sealed class InterestScheduleInEffectProblem()
    : ProblemException(
        ProblemKind.Conflict,
        "interest_schedule_in_effect",
        "Interest rate schedule is in effect",
        "This rate may already apply to accrued days, so it can't be changed. Add a new schedule with a later date instead.");
