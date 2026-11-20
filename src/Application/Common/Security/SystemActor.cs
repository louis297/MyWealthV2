namespace MyWealthV2.Application.Common.Security;

/// <summary>The name a flow writes to the audit columns when no person acts: the worker's system user
/// (deposit-matching R32).</summary>
public static class SystemActor
{
    public const string Name = "system";
}
