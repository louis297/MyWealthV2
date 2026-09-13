using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Tenants.Commands.EnableTenant;

[Authorize(Policy = Policies.TenantsManage)]
public class EnableTenantCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class EnableTenantCommandValidator : AbstractValidator<EnableTenantCommand>
{
    public EnableTenantCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class EnableTenantCommandHandler(IApplicationDbContext db) : IRequestHandler<EnableTenantCommand>
{
    public async Task Handle(EnableTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.Id);

        var expected = Convert.ToBase64String(tenant.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        tenant.Enable();
        await db.SaveChangesAsync(cancellationToken);
    }
}
