using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.BankFeed;

internal static class TenantDisabler
{
    public static async Task DisableAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Tenants.SingleAsync(row => row.Id == tenantId)).Disable();
        await db.SaveChangesAsync();
    }
}
