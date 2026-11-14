using MyWealthV2.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Web.Infrastructure;

/// <summary>
/// Converts well-known application exceptions into RFC 9110-compliant <see cref="ProblemDetails"/> responses,
/// mapping <see cref="ValidationException"/> → 400, <see cref="NotFoundException"/> → 404,
/// <see cref="UnauthorizedAccessException"/> → 401, <see cref="ForbiddenAccessException"/> → 403, and every
/// <see cref="ProblemException"/> → 400 or 409 with its problem code (api-design §4.2), and a request body that
/// cannot be read → 400 (an instant without an offset → <c>errors.&lt;Field&gt;</c>, fixes/utc-instants.md).
/// Unrecognised exceptions are not handled and fall through to the default middleware.
/// </summary>
public class ProblemDetailsExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, problemDetails) = exception switch
        {
            ValidationException ve => (StatusCodes.Status400BadRequest, (ProblemDetails)new ValidationProblemDetails(ve.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            }),
            NotFoundException ne => (StatusCodes.Status404NotFound, new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                Title = "The specified resource was not found.",
                Detail = ne.Message
            }),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2"
            }),
            ForbiddenAccessException => (StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4"
            }),
            ProblemException problem => (StatusOf(problem.Kind), CodedProblem(problem)),
            BadHttpRequestException { InnerException: InstantFormatException instant } =>
                (StatusCodes.Status400BadRequest, new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    [FieldOf(instant.Path)] = [instant.Message]
                })
                {
                    Status = StatusCodes.Status400BadRequest,
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
                }),
            BadHttpRequestException bad => (bad.StatusCode, new ProblemDetails
            {
                Status = bad.StatusCode,
                Title = "Bad request",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            }),
            _ => (-1, null)
        };

        if (problemDetails is null) return false;

        if (problemDetails is ValidationProblemDetails validation)
        {
            problemDetails.Extensions["errors"] = validation.Errors;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    /// <summary>The JSON path without <c>$.</c>, first letter upper-case: the key the required rule uses (<c>$.bookedAt</c> → <c>BookedAt</c>).</summary>
    private static string FieldOf(string? path)
    {
        var name = path is null ? string.Empty : path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$');
        return name.Length == 0 ? string.Empty : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static int StatusOf(ProblemKind kind) => kind switch
    {
        ProblemKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>One shape for every problem code (api-design §4.2 C5).</summary>
    private static ProblemDetails CodedProblem(ProblemException exception)
    {
        var status = StatusOf(exception.Kind);
        var problem = exception.Errors is { } errors
            ? new ValidationProblemDetails(errors.ToDictionary(pair => pair.Key, pair => pair.Value))
            : new ProblemDetails();
        problem.Status = status;
        problem.Type = status == StatusCodes.Status409Conflict
            ? "https://tools.ietf.org/html/rfc9110#section-15.5.10"
            : "https://tools.ietf.org/html/rfc9110#section-15.5.1";
        problem.Title = exception.Title;
        problem.Detail = exception.Detail;
        problem.Extensions["code"] = exception.Code;
        foreach (var (name, value) in exception.Extensions)
        {
            problem.Extensions[name] = value;
        }

        return problem;
    }
}
