using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Events;
using MyWealthV2.Domain.Exceptions;

namespace MyWealthV2.Domain.Entities;

public class User : BaseAuditableEntity
{
    private User()
    {
    }

    public UserStatus Status { get; private set; }

    public UserRole Role { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string IdentityUserId { get; private set; } = string.Empty;

    public int? TenantId { get; private set; }

    public int? AdviserId { get; private set; }

    public Guid PublicId { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

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
        if (string.IsNullOrWhiteSpace(identityUserId))
        {
            throw new DomainException("IdentityUserId is required.");
        }

        EnsureRoleShape(role, tenantId, adviserId);

        var user = new User
        {
            Role = role,
            Name = name,
            Email = email,
            IdentityUserId = identityUserId,
            TenantId = tenantId,
            AdviserId = adviserId,
            PublicId = publicId ?? Guid.NewGuid(),
            Status = withPassword ? UserStatus.Active : UserStatus.PendingActivation
        };

        user.AddDomainEvent(new UserCreated(user));

        if (user.Status == UserStatus.Active)
        {
            user.AddDomainEvent(new UserActivated(user));
        }

        return user;
    }

    public void DisableAdviser(bool hasNonDisabledAssignedCustomers)
    {
        if (Role != UserRole.Adviser)
        {
            throw new DomainException("Only an Adviser can be disabled with DisableAdviser.");
        }

        if (hasNonDisabledAssignedCustomers)
        {
            throw new DomainException(
                "Reassign or disable assigned customers before disabling this adviser.");
        }

        Disable();
    }

    public void Disable()
    {
        if (Status is not (UserStatus.Active or UserStatus.PendingActivation))
        {
            throw new DomainException("Only Active or PendingActivation users can be disabled.");
        }

        Status = UserStatus.Disabled;
        AddDomainEvent(new UserDisabled(this));
    }

    public void Enable()
    {
        if (Status != UserStatus.Disabled)
        {
            throw new DomainException("Only Disabled users can be enabled.");
        }

        Status = UserStatus.Active;
        AddDomainEvent(new UserActivated(this));
    }

    public void ChangeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name is required.");
        }

        Name = name;
    }

    public void RecordPasswordChanged()
    {
        AddDomainEvent(new UserPasswordChanged(this));
    }

    public void Activate()
    {
        if (Status != UserStatus.PendingActivation)
        {
            throw new DomainException("Only PendingActivation users can be activated.");
        }

        Status = UserStatus.Active;
        AddDomainEvent(new UserActivated(this));
    }

    private static void EnsureRoleShape(UserRole role, int? tenantId, int? adviserId)
    {
        var valid = role switch
        {
            UserRole.SystemAdmin => tenantId is null && adviserId is null,
            UserRole.TenantAdmin or UserRole.Adviser => tenantId is not null && adviserId is null,
            UserRole.Customer => tenantId is not null && adviserId is not null,
            _ => false
        };

        if (!valid)
        {
            throw new DomainException($"Role {role} does not match TenantId/AdviserId shape.");
        }
    }
}
