namespace MyWealthV2.Domain.Events;

public class PortfolioAssigned : BaseEvent
{
    public PortfolioAssigned(AccountPortfolioAssignment assignment)
    {
        Assignment = assignment;
    }

    public AccountPortfolioAssignment Assignment { get; }
}
