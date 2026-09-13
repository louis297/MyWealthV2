using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Users.Commands.CreateAdviser;

[Authorize(Policy = Policies.AdvisersManage)]
public class CreateAdviserCommand : IRequest<Guid>
{
    public required string Name { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public Guid? TenantId { get; init; }

    public string? Role { get; init; }

    public Guid? AdviserId { get; init; }
}

public class CreateAdviserCommandValidator : AbstractValidator<CreateAdviserCommand>
{
    public CreateAdviserCommandValidator()
    {
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200)
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.Email)
            .Must(email => !string.IsNullOrWhiteSpace(email))
            .WithMessage("Email is required.")
            .EmailAddress()
            .WithMessage("Email must look like an email.");
        RuleFor(command => command.Password).NotEmpty().MinimumLength(8);
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set.");
        RuleFor(command => command.Role)
            .Must(role => role is null)
            .WithMessage("Role cannot be set.");
        RuleFor(command => command.AdviserId)
            .Must(adviserId => adviserId is null)
            .WithMessage("AdviserId cannot be set.");
    }
}

public class CreateAdviserCommandHandler(
    IApplicationDbContext db,
    IIdentityService identityService,
    ICurrentUser currentUser)
    : IRequestHandler<CreateAdviserCommand, Guid>
{
    public async Task<Guid> Handle(CreateAdviserCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var email = request.Email.Trim();

        var tenantId = currentUser.TenantId
                       ?? throw new NotFoundException("Tenant", "current");

        var tenant = await db.Tenants.SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant", tenantId);

        if (!tenant.IsEnabled)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create an Adviser while the tenant is disabled. Enable the tenant first.");
        }

        if (await db.Users.AnyAsync(
                person => person.TenantId == tenant.Id && person.Email == email, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateAdviserCommand.Email),
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
                        new ValidationFailure(nameof(CreateAdviserCommand.Password), error)));
            }

            var person = User.Create(
                UserRole.Adviser,
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
