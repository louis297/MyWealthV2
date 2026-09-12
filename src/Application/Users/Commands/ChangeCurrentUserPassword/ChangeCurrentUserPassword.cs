using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Application.Users.Commands.ChangeCurrentUserPassword;

public class ChangeCurrentUserPasswordCommand : IRequest
{
    public required string CurrentPassword { get; init; }

    public required string NewPassword { get; init; }
}

public class ChangeCurrentUserPasswordCommandValidator : AbstractValidator<ChangeCurrentUserPasswordCommand>
{
    public ChangeCurrentUserPasswordCommandValidator()
    {
        RuleFor(command => command.CurrentPassword).NotEmpty();
        RuleFor(command => command.NewPassword).NotEmpty().MinimumLength(8);
    }
}

public class ChangeCurrentUserPasswordCommandHandler(
    ICurrentUser currentUser,
    IApplicationDbContext db,
    IIdentityService identityService) : IRequestHandler<ChangeCurrentUserPasswordCommand>
{
    public async Task Handle(ChangeCurrentUserPasswordCommand request, CancellationToken cancellationToken)
    {
        var person = currentUser.Person ?? throw new UnauthorizedAccessException();

        var user = await db.Users.SingleOrDefaultAsync(row => row.Id == person.Id, cancellationToken)
                   ?? throw new UnauthorizedAccessException();

        var result = await identityService.ChangePasswordAsync(
            user.IdentityUserId,
            request.CurrentPassword,
            request.NewPassword);

        if (!result.Succeeded)
        {
            throw new Common.Exceptions.ValidationException();
        }

        user.RecordPasswordChanged();
        await db.SaveChangesAsync(cancellationToken);
    }
}
