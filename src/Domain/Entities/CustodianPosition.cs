namespace MyWealthV2.Domain.Entities;

/// <summary>The custodian's quantity of one symbol in a safekeeping account, as the last stored page sent it
/// (in-kind-transfers R12). Replaced as a set with every page; never computed from the movements.</summary>
public class CustodianPosition
{
    public const int SymbolMaxLength = 32;

    private CustodianPosition()
    {
    }

    public int Id { get; private set; }

    public int TenantId { get; private set; }

    public int SafekeepingAccountId { get; private set; }

    public string Symbol { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public static CustodianPosition Create(int tenantId, int safekeepingAccountId, string symbol, decimal quantity) =>
        throw new NotImplementedException();
}
