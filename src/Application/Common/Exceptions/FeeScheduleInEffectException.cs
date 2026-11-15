namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>
/// The trade fee schedule row may already apply to a posted trade, so it cannot be updated or deleted
/// (api-design §4.2 <c>fee_schedule_in_effect</c>; trade-fees R11).
/// </summary>
public sealed class FeeScheduleInEffectException()
    : ProblemException(
        ProblemKind.Conflict,
        "fee_schedule_in_effect",
        "Fee schedule is in effect",
        "This schedule may already apply to posted trades, so it can't be changed. Add a new schedule with a later date instead.");
