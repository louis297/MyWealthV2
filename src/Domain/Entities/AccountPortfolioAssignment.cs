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
        if (string.IsNullOrWhiteSpace(assignedBy))
        {
            throw new DomainException("AssignedBy is required.");
        }

        if (current is not null && current.AccountId != accountId)
        {
            throw new DomainException("Assignment is for a different account.");
        }

        if (current is { Status: AssignmentStatus.Active } && current.PortfolioId == portfolioId)
        {
            throw new DomainException("Account already has this portfolio assigned.");
        }

        if (current is { Status: AssignmentStatus.Active })
        {
            current.Remove();
        }

        var assignment = new AccountPortfolioAssignment
        {
            PublicId = publicId ?? Guid.NewGuid(),
            TenantId = tenantId,
            AccountId = accountId,
            PortfolioId = portfolioId,
            Status = AssignmentStatus.Active,
            AssignedAt = assignedAt,
            AssignedBy = assignedBy.Trim()
        };
        assignment.AddDomainEvent(new PortfolioAssigned(assignment));
        return assignment;
    }

    public void Remove()
    {
        if (Status == AssignmentStatus.Removed)
        {
            return;
        }

        Status = AssignmentStatus.Removed;
        AddDomainEvent(new PortfolioUnassigned(this));
    }

    public void RecordRebalance(RebalanceStatus status, string? message, DateTimeOffset at)
    {
        LastRebalanceStatus = status;
        LastRebalanceMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        LastRebalanceAt = at;
    }
}
