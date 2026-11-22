using Hangfire;

namespace MyWealthV2.Infrastructure.CorporateActions;

/// <summary>One run of <c>corporate-action-apply</c> (corporate-actions R24).</summary>
public sealed class CorporateActionApplyJob
{
    public const string JobId = "corporate-action-apply";

    public Task Run(CancellationToken cancellationToken) => throw new NotImplementedException();
}

/// <summary>Registers <c>corporate-action-apply</c> (R24).</summary>
public static class CorporateActionApplyRecurringJob
{
    public static void Register(IRecurringJobManager recurring) => throw new NotImplementedException();
}
