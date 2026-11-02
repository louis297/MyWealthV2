namespace MyWealthV2.Domain.Events;

public class PortfolioUnassigned : BaseEvent
{
    public PortfolioUnassigned(AccountPortfolioAssignment assignment)
    {
        Assignment = assignment;
    }

    public AccountPortfolioAssignment Assignment { get; }
}
