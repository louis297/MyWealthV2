using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Users.Commands.CreateTenantAdmin;

[Authorize(Policy = Policies.TenantAdminsManage)]
public class CreateTenantAdminCommand : IRequest<Guid>
{
    public Guid TenantId { get; init; }

    public required string Name { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public bool? IsActive { get; init; }
}

public class CreateTenantAdminCommandValidator : AbstractValidator<CreateTenantAdminCommand>
{
    public CreateTenantAdminCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200)
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.Email)
            .Must(email => !string.IsNullOrWhiteSpace(email))
            .WithMessage("Email is required.")
            .EmailAddress()
            .WithMessage("Email must look like an email.");
        RuleFor(command => command.Password).NotEmpty().MinimumLength(8);
        RuleFor(command => command.IsActive)
            .Must(isActive => isActive is null)
            .WithMessage("IsActive cannot be set.");
    }
}

public class CreateTenantAdminCommandHandler(IApplicationDbContext db, IIdentityService identityService)
    : IRequestHandler<CreateTenantAdminCommand, Guid>
{
    public async Task<Guid> Handle(CreateTenantAdminCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var email = request.Email.Trim();

        var tenant = await db.Tenants.SingleOrDefaultAsync(
                         row => row.PublicId == request.TenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.TenantId);

        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create a TenantAdmin while the tenant is disabled. Enable the tenant first.");
        }

        if (await db.Users.AnyAsync(
                person => person.TenantId == tenant.Id && person.Email == email, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateTenantAdminCommand.Email),
                    "Email must be unique inside the tenant.")
            ]);
        }

        var publicId = Guid.NewGuid();
        await db.ExecuteInTransactionAsync(async token =>
        {
            var (result, identityUserId) = await identityService.CreateLoginAsync(
                publicId.ToString(), email, request.Password, tenant.Id);
            if (!result.Succeeded)
            {
                throw new ValidationException(
                    result.Errors.Select(error =>
                        new ValidationFailure(nameof(CreateTenantAdminCommand.Password), error)));
            }

            var person = User.Create(
                UserRole.TenantAdmin,
                name,
                email,
                identityUserId,
                tenant.Id,
                withPassword: true,
                publicId: publicId);
            db.Users.Add(person);
            await db.SaveChangesAsync(token);
        }, cancellationToken);

        return publicId;
    }
}
