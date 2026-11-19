using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Infrastructure.Identity;

/// <summary>The <see cref="IUser"/> of the Worker, which runs the background jobs.</summary>
public class SystemUser : IUser
{
    /// <summary>The system name the background jobs write to the audit columns, as the seeds do.</summary>
    public const string Name = "system";

    public string? Id => null;
    public List<string>? Roles => null;
}
