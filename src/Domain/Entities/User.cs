using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Domain.Entities;

public class User : BaseAuditableEntity
{
    public UserStatus Status { get; private set; }

    public static User Create(
        UserRole role,
        string name,
        string email,
        string identityUserId,
        int? tenantId = null,
        int? adviserId = null,
        bool withPassword = true,
        Guid? publicId = null)
    {
        throw new NotImplementedException();
    }

    public void Disable()
    {
        throw new NotImplementedException();
    }

    public void Enable()
    {
        throw new NotImplementedException();
    }

    public void Activate()
    {
        throw new NotImplementedException();
    }
}
