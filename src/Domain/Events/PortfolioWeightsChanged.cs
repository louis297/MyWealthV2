namespace MyWealthV2.Domain.Events;

public class PortfolioWeightsChanged : BaseEvent
{
    public PortfolioWeightsChanged(Portfolio portfolio)
    {
        Portfolio = portfolio;
    }

    public Portfolio Portfolio { get; }
}
