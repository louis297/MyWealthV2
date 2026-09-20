namespace MyWealthV2.Application.Common.Interfaces;

public interface IFxRate
{
    decimal GetRate(string from, string to, DateTimeOffset? asOf = null);
}
