using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Users.Commands.EnableAdviser;

[Authorize(Policy = Policies.AdvisersManage)]
public class EnableAdviserCommand : IRequest
{
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class EnableAdviserCommandValidator : AbstractValidator<EnableAdviserCommand>
{
    public EnableAdviserCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
    }
}

public class EnableAdviserCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<EnableAdviserCommand>
{
    public async Task Handle(EnableAdviserCommand request, CancellationToken cancellationToken)
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

        if (person.Status == UserStatus.Active)
        {
            return;
        }

        person.Enable();
        await db.SaveChangesAsync(cancellationToken);
    }
}
