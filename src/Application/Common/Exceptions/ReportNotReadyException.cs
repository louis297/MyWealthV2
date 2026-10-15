namespace MyWealthV2.Application.Common.Exceptions;

public sealed class ReportNotReadyException : Exception
{
    public ReportNotReadyException()
        : base("The report is not ready.")
    {
    }
}
