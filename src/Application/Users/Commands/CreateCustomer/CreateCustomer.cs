using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Users.Commands.CreateCustomer;

[Authorize(Policy = Policies.CustomersManage + "," + Policies.CustomersManageOwn)]
public class CreateCustomerCommand : IRequest<Guid>
{
    public required string Name { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public Guid? AdviserId { get; init; }

    public Guid? TenantId { get; init; }

    public string? Role { get; init; }
}

public class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator(ICurrentUser currentUser)
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

        When(_ => currentUser.Role is { } role && RolePermissions.Has(role, Policies.CustomersManage), () =>
        {
            RuleFor(command => command.AdviserId)
                .NotEmpty()
                .WithMessage("AdviserId is required.");
        });

        When(_ => currentUser.Role is { } role
                  && RolePermissions.Has(role, Policies.CustomersManageOwn)
                  && !RolePermissions.Has(role, Policies.CustomersManage), () =>
        {
            RuleFor(command => command.AdviserId)
                .Must(adviserId => adviserId is null || adviserId == currentUser.PublicId)
                .WithMessage("An Adviser may only assign a Customer to themselves.");
        });
    }
}

public class CreateCustomerCommandHandler(
    IApplicationDbContext db,
    IIdentityService identityService,
    ICurrentUser currentUser)
    : IRequestHandler<CreateCustomerCommand, Guid>
{
    public async Task<Guid> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var email = request.Email.Trim();

        var tenantId = currentUser.TenantId
                       ?? throw new NotFoundException("Tenant", "current");

        var tenant = await db.Tenants.SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant", tenantId);

        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create a Customer while the tenant is disabled. Enable the tenant first.");
        }

        var adviser = await ResolveAdviserAsync(request.AdviserId, tenant.Id, cancellationToken);

        if (adviser.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                adviser.PublicId,
                "User is disabled",
                "Cannot create a Customer on a disabled adviser. Enable or pick another adviser.");
        }

        if (await db.Users.AnyAsync(
                person => person.TenantId == tenant.Id && person.Email == email, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateCustomerCommand.Email),
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
                        new ValidationFailure(nameof(CreateCustomerCommand.Password), error)));
            }

            var person = User.Create(
                UserRole.Customer,
                name,
                email,
                identityUserId,
                tenant.Id,
                adviser.Id,
                withPassword: true,
                publicId: publicId);
            db.Users.Add(person);
            await db.SaveChangesAsync(token);
        }, cancellationToken);

        return publicId;
    }

    private async Task<User> ResolveAdviserAsync(
        Guid? adviserPublicId,
        int tenantId,
        CancellationToken cancellationToken)
    {
        if (currentUser.Role is { } role && RolePermissions.Has(role, Policies.CustomersManage))
        {
            var publicId = adviserPublicId
                           ?? throw new NotFoundException("Adviser", "current");
            return await db.Users.SingleOrDefaultAsync(
                       row => row.PublicId == publicId
                              && row.Role == UserRole.Adviser
                              && row.TenantId == tenantId,
                       cancellationToken)
                   ?? throw new NotFoundException("Adviser", publicId);
        }

        return currentUser.Person
               ?? throw new NotFoundException("Adviser", "current");
    }
}
