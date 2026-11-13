namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>The write targets a tenant, user or account that exists but is disabled (api-design §4.1, code <c>disabled</c>).</summary>
public sealed class TargetDisabledException(string target, Guid targetId, string title, string detail)
    : ProblemException(ProblemKind.Rule, "disabled", title, detail)
{
    public string Target { get; } = target;

    public Guid TargetId { get; } = targetId;

    public override IReadOnlyDictionary<string, object?> Extensions => new Dictionary<string, object?>
    {
        ["target"] = Target,
        ["targetId"] = TargetId
    };
}
