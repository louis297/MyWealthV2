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
    }
}
