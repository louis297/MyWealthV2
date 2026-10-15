namespace MyWealthV2.Domain.Entities;

public class Report : BaseEntity
{
    private Report()
    {
    }

    public Guid PublicId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Function { get; private set; } = string.Empty;

    public string Scope { get; private set; } = string.Empty;

    public string AllowedTriggers { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }
}
