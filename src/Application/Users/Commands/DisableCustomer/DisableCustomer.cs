using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Users.Commands.DisableCustomer;

[Authorize(Policy = Policies.CustomersManage + "," + Policies.CustomersManageOwn)]
public class DisableCustomerCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class DisableCustomerCommandValidator : AbstractValidator<DisableCustomerCommand>
{
    public DisableCustomerCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class DisableCustomerCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DisableCustomerCommand>
{
    public async Task Handle(DisableCustomerCommand request, CancellationToken cancellationToken)
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

        if (person.Status == UserStatus.Disabled)
        {
            return;
        }

        if (await db.Accounts.AnyAsync(
                account => account.CustomerId == person.Id && account.IsActive, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty,
                    "Cannot disable a customer who still has an active account.")
            ]);
        }

        person.Disable();
        await db.SaveChangesAsync(cancellationToken);
    }
}
