namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>Category of a coded refusal. The Web layer maps it to an HTTP status (api-design §4.2 C9).</summary>
public enum ProblemKind
{
    /// <summary>A named business rule refused the request (400).</summary>
    Rule,

    /// <summary>The request conflicts with the current state (409).</summary>
    Conflict
}

/// <summary>
/// A refusal with a stable problem code (api-design §4.2). Every code in the §4.2 registry is one subclass.
/// <c>webapi</c> maps every subclass in one place, so a new code needs no change outside Application.
/// </summary>
public abstract class ProblemException : Exception
{
    private static readonly IReadOnlyDictionary<string, object?> NoExtensions = new Dictionary<string, object?>();

    protected ProblemException(ProblemKind kind, string code, string title, string? detail = null, string? message = null)
        : base(message ?? detail ?? title)
    {
        Kind = kind;
        Code = code;
        Title = title;
        Detail = detail;
    }

    public ProblemKind Kind { get; }

    /// <summary>Lower snake_case code from the §4.2 registry. Never renamed or reused.</summary>
    public string Code { get; }

    /// <summary>Fixed for the code.</summary>
    public string Title { get; }

    /// <summary>This request. Safe to show to the caller.</summary>
    public string? Detail { get; }

    /// <summary>Field messages written as <c>errors</c>; only for codes whose registry row lists them.</summary>
    public virtual IReadOnlyDictionary<string, string[]>? Errors => null;

    /// <summary>Other extension fields; only for codes whose registry row lists them.</summary>
    public virtual IReadOnlyDictionary<string, object?> Extensions => NoExtensions;
}
