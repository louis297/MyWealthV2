namespace MyWealthV2.Infrastructure.Data.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public int? TenantId { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTimeOffset? ProcessedAt { get; set; }

    public string? Error { get; set; }

    public int RetryCount { get; set; }

    public DateTimeOffset Created { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    public string? LastModifiedBy { get; set; }

    public byte[] RowVersion { get; set; } = null!;
}
