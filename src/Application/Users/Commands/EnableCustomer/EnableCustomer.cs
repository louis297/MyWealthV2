using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Users.Commands.EnableCustomer;

[Authorize(Policy = Policies.CustomersManage + "," + Policies.CustomersManageOwn)]
public class EnableCustomerCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class EnableCustomerCommandValidator : AbstractValidator<EnableCustomerCommand>
{
    public EnableCustomerCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class EnableCustomerCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<EnableCustomerCommand>
{
    public async Task Handle(EnableCustomerCommand request, CancellationToken cancellationToken)
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

        if (person.Status == UserStatus.Active)
        {
            return;
        }

        person.Enable();
        await db.SaveChangesAsync(cancellationToken);
    }
}
