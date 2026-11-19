using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.ClientMoneyAccounts.Commands.CreateClientMoneyAccount;

[Authorize(Policy = Policies.TenantsManage)]
public class CreateClientMoneyAccountCommand : IRequest<Guid>
{
    /// <summary>The tenant from the route. Not read from the body.</summary>
    [JsonIgnore]
    public Guid RouteTenantId { get; set; }

    public string? Currency { get; init; }

    public string? BankName { get; init; }

    public string? AccountName { get; init; }

    public string? AccountNumber { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public Guid? Id { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public bool? IsActive { get; init; }
}

public class CreateClientMoneyAccountCommandHandler
    : IRequestHandler<CreateClientMoneyAccountCommand, Guid>
{
    public Task<Guid> Handle(CreateClientMoneyAccountCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
