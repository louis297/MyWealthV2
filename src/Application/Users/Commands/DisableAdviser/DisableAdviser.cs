using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Users.Commands.DisableAdviser;

[Authorize(Policy = Policies.AdvisersManage)]
public class DisableAdviserCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class DisableAdviserCommandValidator : AbstractValidator<DisableAdviserCommand>
{
    public DisableAdviserCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class DisableAdviserCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<DisableAdviserCommand>
{
    public async Task Handle(DisableAdviserCommand request, CancellationToken cancellationToken)
    {
        var person = await db.Users.SingleOrDefaultAsync(
                         row => row.PublicId == request.Id
                                && row.Role == UserRole.Adviser
                                && row.TenantId == currentUser.TenantId,
                         cancellationToken)
                     ?? throw new NotFoundException("Adviser", request.Id);

        var expected = Convert.ToBase64String(person.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        if (person.Status == UserStatus.Disabled)
        {
            return;
        }

        var hasNonDisabledAssignedCustomers = await db.Users.AnyAsync(
            row => row.AdviserId == person.Id
                   && row.Role == UserRole.Customer
                   && row.Status != UserStatus.Disabled,
            cancellationToken);

        try
        {
            person.DisableAdviser(hasNonDisabledAssignedCustomers);
        }
        catch (DomainException)
        {
            throw new ValidationException([
                new ValidationFailure(
                    string.Empty,
                    "Reassign or disable assigned customers before disabling this adviser.")
            ]);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
