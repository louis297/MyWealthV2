using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Users.Commands.DisableTenantAdmin;

[Authorize(Policy = Policies.TenantAdminsManage)]
public class DisableTenantAdminCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class DisableTenantAdminCommandValidator : AbstractValidator<DisableTenantAdminCommand>
{
    public DisableTenantAdminCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class DisableTenantAdminCommandHandler(IApplicationDbContext db)
    : IRequestHandler<DisableTenantAdminCommand>
{
    public async Task Handle(DisableTenantAdminCommand request, CancellationToken cancellationToken)
    {
        var person = await db.Users.SingleOrDefaultAsync(
                         row => row.PublicId == request.Id && row.Role == UserRole.TenantAdmin,
                         cancellationToken)
                     ?? throw new NotFoundException("TenantAdmin", request.Id);

        var expected = Convert.ToBase64String(person.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        if (person.Status == UserStatus.Disabled)
        {
            return;
        }

        person.Disable();
        await db.SaveChangesAsync(cancellationToken);
    }
}
