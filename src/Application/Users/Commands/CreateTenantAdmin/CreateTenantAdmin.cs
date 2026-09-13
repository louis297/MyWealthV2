using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Users.Commands.CreateTenantAdmin;

[Authorize(Policy = Policies.TenantAdminsManage)]
public class CreateTenantAdminCommand : IRequest<Guid>
{
    public Guid TenantId { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }
}

public class CreateTenantAdminCommandHandler : IRequestHandler<CreateTenantAdminCommand, Guid>
{
    public Task<Guid> Handle(CreateTenantAdminCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
