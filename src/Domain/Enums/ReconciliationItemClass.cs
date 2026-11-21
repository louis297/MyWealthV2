namespace MyWealthV2.Domain.Enums;

/// <summary>
/// The class of a reconciliation item: a break someone must act on, a timing difference that clears by itself, or a
/// known difference that is explained and accepted (bank-reconciliation R9–R12).
/// </summary>
public enum ReconciliationItemClass
{
    Break = 0,
    Timing = 1,
    Known = 2
}
