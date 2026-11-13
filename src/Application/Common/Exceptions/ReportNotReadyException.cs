namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>The report execution has no result to download yet (api-design §4.2 <c>report_not_ready</c>).</summary>
public sealed class ReportNotReadyException()
    : ProblemException(ProblemKind.Conflict, "report_not_ready", "Conflict", "The report is not ready.");
