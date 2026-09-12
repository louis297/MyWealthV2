using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Application.Users.Commands.UpdateCurrentUser;

public class UpdateCurrentUserCommand : IRequest
{
    public required string Name { get; init; }

    public required string RowVersion { get; init; }

    public string? Email { get; init; }

    public string? Role { get; init; }

    public Guid? TenantId { get; init; }

    public string? Status { get; init; }

    public Guid? AdviserId { get; init; }
}

public class UpdateCurrentUserCommandValidator : AbstractValidator<UpdateCurrentUserCommand>
{
    public UpdateCurrentUserCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
        RuleFor(command => command)
            .Must(command => command.Email is null
                             && command.Role is null
                             && command.TenantId is null
                             && command.Status is null
                             && command.AdviserId is null)
            .WithMessage("Only name can be updated.");
    }
}

public class UpdateCurrentUserCommandHandler(ICurrentUser currentUser, IApplicationDbContext db)
    : IRequestHandler<UpdateCurrentUserCommand>
{
    public async Task Handle(UpdateCurrentUserCommand request, CancellationToken cancellationToken)
    {
        var person = currentUser.Person ?? throw new UnauthorizedAccessException();

        var user = await db.Users.SingleOrDefaultAsync(row => row.Id == person.Id, cancellationToken)
                   ?? throw new UnauthorizedAccessException();

        var expected = Convert.ToBase64String(user.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        user.ChangeName(request.Name);
        await db.SaveChangesAsync(cancellationToken);
    }
}
