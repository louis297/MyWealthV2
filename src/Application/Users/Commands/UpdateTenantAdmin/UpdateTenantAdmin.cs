using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Users.Commands.UpdateTenantAdmin;

[Authorize(Policy = Policies.TenantAdminsManage)]
public class UpdateTenantAdminCommand : IRequest
{
    public Guid Id { get; set; }

    public string? Name { get; init; }

    public string? RowVersion { get; init; }

    public string? Email { get; init; }

    public Guid? TenantId { get; init; }

    public string? Role { get; init; }

    public string? Password { get; init; }

    public bool? IsActive { get; init; }
}

public class UpdateTenantAdminCommandValidator : AbstractValidator<UpdateTenantAdminCommand>
{
    public UpdateTenantAdminCommandValidator()
    {
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200)
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Email)
            .Must(email => email is null)
            .WithMessage("Email cannot be changed.");
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be changed.");
        RuleFor(command => command.Role)
            .Must(role => role is null)
            .WithMessage("Role cannot be changed.");
        RuleFor(command => command.Password)
            .Must(password => password is null)
            .WithMessage("Password cannot be changed.");
        RuleFor(command => command.IsActive)
            .Must(isActive => isActive is null)
            .WithMessage("IsActive cannot be changed.");
    }
}

public class UpdateTenantAdminCommandHandler(IApplicationDbContext db)
    : IRequestHandler<UpdateTenantAdminCommand>
{
    public async Task Handle(UpdateTenantAdminCommand request, CancellationToken cancellationToken)
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

        person.ChangeName(request.Name!.Trim());
        await db.SaveChangesAsync(cancellationToken);
    }
}
