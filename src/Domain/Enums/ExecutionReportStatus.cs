namespace MyWealthV2.Domain.Enums;

/// <summary>Where a stored execution report is (orders R24, R25).</summary>
public enum ExecutionReportStatus
{
    Received = 0,
    Processed = 1,
    Skipped = 2
}
