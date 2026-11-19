using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.ClientMoneyAccounts.Commands.EnableClientMoneyAccount;

[Authorize(Policy = Policies.TenantsManage)]
public class EnableClientMoneyAccountCommand : IRequest
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class EnableClientMoneyAccountCommandHandler
    : IRequestHandler<EnableClientMoneyAccountCommand>
{
    public Task Handle(EnableClientMoneyAccountCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
