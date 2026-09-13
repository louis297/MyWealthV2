using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Tenants.Commands.DisableTenant;

[Authorize(Policy = Policies.TenantsManage)]
public class DisableTenantCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class DisableTenantCommandValidator : AbstractValidator<DisableTenantCommand>
{
    public DisableTenantCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class DisableTenantCommandHandler(IApplicationDbContext db) : IRequestHandler<DisableTenantCommand>
{
    public async Task Handle(DisableTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.Id);

        var expected = Convert.ToBase64String(tenant.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        tenant.Disable();
        await db.SaveChangesAsync(cancellationToken);
    }
}
