using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.ClientMoneyAccounts.Commands.DisableClientMoneyAccount;

/// <summary>Disables a client money account (R8). Always allowed.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public class DisableClientMoneyAccountCommand : IRequest
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class DisableClientMoneyAccountCommandValidator : AbstractValidator<DisableClientMoneyAccountCommand>
{
    public DisableClientMoneyAccountCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class DisableClientMoneyAccountCommandHandler(IApplicationDbContext db)
    : IRequestHandler<DisableClientMoneyAccountCommand>
{
    public async Task Handle(DisableClientMoneyAccountCommand request, CancellationToken cancellationToken)
    {
        var row = await ClientMoneyAccountLookup.FindAsync(db, request.TenantId, request.Id, cancellationToken);

        var expected = Convert.ToBase64String(row.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        if (!row.IsActive)
        {
            return;
        }

        row.Disable();
        await db.SaveChangesAsync(cancellationToken);
    }
}
