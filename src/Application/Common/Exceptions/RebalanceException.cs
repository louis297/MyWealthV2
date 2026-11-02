namespace MyWealthV2.Application.Common.Exceptions;

public sealed class RebalanceException : Exception
{
    public RebalanceException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
