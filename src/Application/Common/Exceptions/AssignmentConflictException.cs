namespace MyWealthV2.Application.Common.Exceptions;

public sealed class AssignmentConflictException : Exception
{
    public AssignmentConflictException()
        : base("Account already has this portfolio assigned.")
    {
    }
}
