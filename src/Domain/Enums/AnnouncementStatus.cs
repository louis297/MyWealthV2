namespace MyWealthV2.Domain.Enums;

/// <summary>Where a stored corporate action announcement is in processing (corporate-actions R10, R11).</summary>
public enum AnnouncementStatus
{
    Received = 0,
    Processed = 1,
    Skipped = 2
}
