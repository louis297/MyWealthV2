using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.ClientMoneyAccounts.Commands.DisableClientMoneyAccount;

[Authorize(Policy = Policies.TenantsManage)]
public class DisableClientMoneyAccountCommand : IRequest
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class DisableClientMoneyAccountCommandHandler
    : IRequestHandler<DisableClientMoneyAccountCommand>
{
    public Task Handle(DisableClientMoneyAccountCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
