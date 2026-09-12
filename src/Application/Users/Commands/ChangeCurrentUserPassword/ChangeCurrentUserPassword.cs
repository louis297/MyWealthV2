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
    }
}
