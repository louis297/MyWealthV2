namespace MyWealthV2.Domain.Enums;

/// <summary>How a statement line was matched: by its deposit reference, or by a TenantAdmin (deposit-matching R17, R26).</summary>
public enum StatementLineMatchMethod
{
    Reference = 0,
    Manual = 1
}
