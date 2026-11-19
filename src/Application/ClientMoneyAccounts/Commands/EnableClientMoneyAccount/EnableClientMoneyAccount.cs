using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.ClientMoneyAccounts.Commands.EnableClientMoneyAccount;

/// <summary>Enables a client money account again after the R4 and R5 checks (R8).</summary>
[Authorize(Policy = Policies.TenantsManage)]
public class EnableClientMoneyAccountCommand : IRequest
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class EnableClientMoneyAccountCommandValidator : AbstractValidator<EnableClientMoneyAccountCommand>
{
    public EnableClientMoneyAccountCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class EnableClientMoneyAccountCommandHandler(IApplicationDbContext db)
    : IRequestHandler<EnableClientMoneyAccountCommand>
{
    public async Task Handle(EnableClientMoneyAccountCommand request, CancellationToken cancellationToken)
    {
        var row = await ClientMoneyAccountLookup.FindAsync(db, request.TenantId, request.Id, cancellationToken);

        var expected = Convert.ToBase64String(row.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        if (row.IsActive)
        {
            return;
        }

        await ClientMoneyAccountRules.EnsureCanBeActiveAsync(
            db, row.TenantId, row.Currency, row.AccountNumber, row.Id, cancellationToken);
        row.Enable();
        await db.SaveChangesAsync(cancellationToken);
    }
}
