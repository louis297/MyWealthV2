using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

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

public sealed class RunBankReconciliationCommandValidator : AbstractValidator<RunBankReconciliationCommand>
{
    public RunBankReconciliationCommandValidator()
    {
        RuleFor(command => command.ClientMoneyAccountId).NotEmpty();
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
    }
}

public sealed class RunBankReconciliationCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReconcileClientMoneyAccount reconcile) : IRequestHandler<RunBankReconciliationCommand, Guid>
{
    public async Task<Guid> Handle(RunBankReconciliationCommand request, CancellationToken cancellationToken)
    {
        var tenantId = BankReconciliationReader.CurrentTenantId(currentUser);
        var account = await BankReconciliationReader.FindAccountAsync(
            db, tenantId, request.ClientMoneyAccountId!.Value, cancellationToken);
        var person = currentUser.Person ?? throw new NotFoundException("User", "current");
        return await reconcile.RunAsync(account.Id, ReconciliationTrigger.Manual, person.Id, cancellationToken);
    }
}
