namespace MyWealthV2.Application.Common.Exceptions;

public sealed class TargetDisabledException : Exception
{
    public TargetDisabledException(string target, Guid targetId, string title, string detail)
        : base(detail)
    {
        Target = target;
        TargetId = targetId;
        Title = title;
        Detail = detail;
    }

    public string Target { get; }

    public Guid TargetId { get; }

    public string Title { get; }

    public string Detail { get; }

    public string Code => "disabled";
}
