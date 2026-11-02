namespace MyWealthV2.Domain.Events;

public class PortfolioCreated : BaseEvent
{
    public PortfolioCreated(Portfolio portfolio)
    {
        Portfolio = portfolio;
    }

    public Portfolio Portfolio { get; }
}
