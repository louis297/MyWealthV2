using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Users.Commands.UpdateCustomer;

[Authorize(Policy = Policies.CustomersManage + "," + Policies.CustomersManageOwn)]
public class UpdateCustomerCommand : IRequest
{
    public Guid Id { get; set; }

    public string? Name { get; init; }

    public string? RowVersion { get; init; }

    public Guid? AdviserId { get; init; }

    public string? Email { get; init; }

    public Guid? TenantId { get; init; }

    public string? Role { get; init; }

    public string? Password { get; init; }

    public bool? IsActive { get; init; }
}

public class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator(ICurrentUser currentUser)
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
        When(_ => !CustomerScope.ManagesTenant(currentUser), () =>
        {
            RuleFor(command => command.AdviserId)
                .Must(adviserId => adviserId is null)
                .WithMessage("AdviserId cannot be changed.");
        });
    }
}

public class UpdateCustomerCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UpdateCustomerCommand>
{
    public async Task Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var assignedAdviserId = CustomerScope.AssignedAdviserInternalId(currentUser);
        var person = await db.Users.SingleOrDefaultAsync(
                         row => row.PublicId == request.Id
                                && row.Role == UserRole.Customer
                                && row.TenantId == currentUser.TenantId
                                && (assignedAdviserId == null || row.AdviserId == assignedAdviserId),
                         cancellationToken)
                     ?? throw new NotFoundException("Customer", request.Id);

        var expected = Convert.ToBase64String(person.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        person.ChangeName(request.Name!.Trim());

        if (request.AdviserId is { } newAdviserPublicId && CustomerScope.ManagesTenant(currentUser))
        {
            var adviser = await db.Users.SingleOrDefaultAsync(
                              row => row.PublicId == newAdviserPublicId
                                     && row.Role == UserRole.Adviser
                                     && row.TenantId == currentUser.TenantId,
                              cancellationToken)
                          ?? throw new NotFoundException("Adviser", newAdviserPublicId);

            if (adviser.Status == UserStatus.Disabled)
            {
                throw new TargetDisabledException(
                    "user",
                    adviser.PublicId,
                    "User is disabled",
                    "Cannot reassign a Customer to a disabled adviser. Enable or pick another adviser.");
            }

            person.ReassignAdviser(adviser.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
