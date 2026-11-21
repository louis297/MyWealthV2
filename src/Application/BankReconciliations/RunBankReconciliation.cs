using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>R17, R18: "Run now" for one client money account of the current tenant, active or not.</summary>
[Authorize(Policy = Policies.BankReconciliationsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RunBankReconciliationCommand : IRequest<Guid>
{
    public Guid? ClientMoneyAccountId { get; init; }

    /// <summary>No caller names a tenant (R1).</summary>
    public Guid? TenantId { get; init; }
}

public sealed class RunBankReconciliationCommandHandler : IRequestHandler<RunBankReconciliationCommand, Guid>
{
    public Task<Guid> Handle(RunBankReconciliationCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
