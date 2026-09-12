using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Common.Interfaces;

public interface ICurrentUser : IUser
{
    Guid? PublicId { get; }

    UserRole? Role { get; }

    int? TenantId { get; }

    User? Person { get; }
}
