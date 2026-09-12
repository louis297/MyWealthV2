namespace MyWealthV2.Application.Common.Exceptions;

public class ConcurrencyException : Exception
{
    public ConcurrencyException() : base("The resource was modified by another request.")
    {
    }
}
