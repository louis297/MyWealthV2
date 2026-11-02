namespace MyWealthV2.Domain.Entities;

public class AccountPortfolioAssignment : BaseAuditableEntity
{
    private AccountPortfolioAssignment()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public int AccountId { get; private set; }

    public int PortfolioId { get; private set; }

    public AssignmentStatus Status { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public string AssignedBy { get; private set; } = string.Empty;

    public DateTimeOffset? LastRebalanceAt { get; private set; }

    public RebalanceStatus? LastRebalanceStatus { get; private set; }

    public string? LastRebalanceMessage { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public static AccountPortfolioAssignment Assign(
        AccountPortfolioAssignment? current,
        int tenantId,
        int accountId,
        int portfolioId,
        string assignedBy,
        DateTimeOffset assignedAt,
        Guid? publicId = null)
    {
        _ = current;
        _ = tenantId;
        _ = accountId;
        _ = portfolioId;
        _ = assignedBy;
        _ = assignedAt;
        _ = publicId;
        return new AccountPortfolioAssignment { Status = AssignmentStatus.Removed };
    }

    public void Remove()
    {
    }

    public void RecordRebalance(RebalanceStatus status, string? message, DateTimeOffset at)
    {
        _ = status;
        _ = message;
        _ = at;
    }
}
