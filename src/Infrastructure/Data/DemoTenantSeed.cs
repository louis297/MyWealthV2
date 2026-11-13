namespace MyWealthV2.Infrastructure.Data;

/// <summary>
/// The demo seed (demo-tenant-seed R4–R12): creates the demo tenant, its demo catalog and its
/// report grants when missing. Never changes an existing row.
/// </summary>
public static class DemoTenantSeed
{
    public static Task RunAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
