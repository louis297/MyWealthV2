namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>The account's Active assignment is already this portfolio (api-design §4.2 <c>portfolio_already_assigned</c>).</summary>
public sealed class AssignmentConflictException()
    : ProblemException(
        ProblemKind.Conflict,
        "portfolio_already_assigned",
        "Conflict",
        "Account already has this portfolio assigned.");
