namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>The request's <c>rowVersion</c> is not the stored one (api-design §4.2 <c>stale_row_version</c>).</summary>
public class ConcurrencyException()
    : ProblemException(
        ProblemKind.Conflict,
        "stale_row_version",
        "Conflict",
        message: "The resource was modified by another request.");
