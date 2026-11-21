using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.BankReconciliations;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.BankReconciliations;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Shared;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Bank-reconciliation R16, R18: the daily recurring job <c>bank-reconciliation</c>.</summary>
public class BankReconciliationJobTests
{
    [Test]
    public void Register_AddsADailyUtcJob_At1700_OnQueueBank()
    {
        using var host = HangfireHost();

        BankReconciliationRecurringJob.Register(host.Services.GetRequiredService<IRecurringJobManager>());

        using var storage = host.Services.GetRequiredService<JobStorage>().GetConnection();
        storage.GetAllItemsFromSet("recurring-jobs").ShouldBe(["bank-reconciliation"]);
        var job = storage.GetAllEntriesFromHash("recurring-job:bank-reconciliation");
        job["Cron"].ShouldBe("0 17 * * *");
        job["TimeZoneId"].ShouldBe("UTC");
        using var payload = JsonDocument.Parse(job["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("bank");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("BankReconciliationJob");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Run");
    }

    [Test]
    public void Run_HasNoAutomaticRetry()
    {
        // Read from metadata: constructing the attribute asks Hangfire's global log provider for a logger.
        var retry = typeof(BankReconciliationJob).GetMethod(nameof(BankReconciliationJob.Run))!
            .CustomAttributes
            .Single(data => data.AttributeType == typeof(AutomaticRetryAttribute));

        retry.NamedArguments.Single(argument => argument.MemberName == nameof(AutomaticRetryAttribute.Attempts))
            .TypedValue.Value.ShouldBe(0);
    }

    [Test]
    public async Task Run_ReconcilesEveryActiveClientMoneyAccount_AlsoOfADisabledTenant_AndSkipsAnInactiveOne()
    {
        var world = await InsertWorldAsync();
        using var host = WorkerHost();

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BankReconciliationJob>().Run(CancellationToken.None);
        }

        await using var db = OpenDb();
        var runs = await db.BankReconciliations.AsNoTracking()
            .Where(row => world.All.Contains(row.ClientMoneyAccountId))
            .ToListAsync();
        runs.Select(row => row.ClientMoneyAccountId).OrderBy(id => id).ShouldBe([world.Active, world.OfDisabledTenant]);
        runs.ShouldAllBe(row => row.Trigger == ReconciliationTrigger.Scheduled && row.RanByUserId == null);
        runs.ShouldAllBe(row => row.Status == BankReconciliationStatus.NoBankBalance);
        runs.ShouldAllBe(row => row.CreatedBy == SystemActor.Name);
    }

    [Test]
    public async Task Run_WithOneAccountFailing_ReconcilesTheOthers_AndThenFails()
    {
        var world = await InsertWorldAsync();
        using var host = WorkerHost(failFor: world.Active);

        using (var scope = host.Services.CreateScope())
        {
            var exception = await Should.ThrowAsync<BankReconciliationFailedException>(() =>
                scope.ServiceProvider.GetRequiredService<BankReconciliationJob>().Run(CancellationToken.None));
            exception.Message.ShouldContain(world.ActivePublicId.ToString());
        }

        await using var db = OpenDb();
        (await db.BankReconciliations.AsNoTracking()
                .Where(row => world.All.Contains(row.ClientMoneyAccountId))
                .Select(row => row.ClientMoneyAccountId)
                .ToListAsync())
            .ShouldBe([world.OfDisabledTenant]);
    }

    private sealed record World(int Active, Guid ActivePublicId, int OfDisabledTenant, int Inactive)
    {
        public int[] All => [Active, OfDisabledTenant, Inactive];
    }

    /// <summary>Tenant A with an active and an inactive USD client money account; disabled tenant B with an active one.
    /// A's active account has a lower Id than B's, so a failure of it comes first.</summary>
    private static async Task<World> InsertWorldAsync()
    {
        await using var db = OpenDb();
        var (tenant, otherTenant) = await TwoTenantFixture.InsertAsync(db);
        var usd = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == "USD");
        var inactive = ClientMoneyAccount.Create(tenant.Id, usd, "Simulator Bank", "Old trust", Number(), "simulator");
        inactive.CreatedBy = "test";
        db.ClientMoneyAccounts.Add(inactive);
        await db.SaveChangesAsync();
        inactive.Disable();
        await db.SaveChangesAsync();
        var active = ClientMoneyAccount.Create(tenant.Id, usd, "Simulator Bank", "Trust", Number(), "simulator");
        active.CreatedBy = "test";
        db.ClientMoneyAccounts.Add(active);
        await db.SaveChangesAsync();
        var other = ClientMoneyAccount.Create(otherTenant.Id, usd, "Simulator Bank", "Trust", Number(), "simulator");
        other.CreatedBy = "test";
        db.ClientMoneyAccounts.Add(other);
        var disabled = await db.Tenants.SingleAsync(row => row.Id == otherTenant.Id);
        disabled.Disable();
        await db.SaveChangesAsync();
        return new World(active.Id, active.PublicId, other.Id, inactive.Id);
    }

    private static string Number() => "REC-" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();

    private static IHost HangfireHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        return builder.Build();
    }

    private static IHost WorkerHost(int? failFor = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] = SchemaDatabaseSetup.ConnectionString;
        builder.AddInfrastructureServices();
        builder.Services.AddScoped<IUser, SystemUser>();
        if (failFor is int id)
        {
            builder.Services.RemoveAll<IReconcileClientMoneyAccount>();
            builder.Services.AddScoped<ReconcileClientMoneyAccount>();
            builder.Services.AddScoped<IReconcileClientMoneyAccount>(sp =>
                new FailingFor(id, sp.GetRequiredService<ReconcileClientMoneyAccount>()));
        }

        return builder.Build();
    }

    private static ApplicationDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(SchemaDatabaseSetup.ConnectionString).Options);

    /// <summary>Fails the run of one client money account and runs the others as the real service does.</summary>
    private sealed class FailingFor(int clientMoneyAccountId, IReconcileClientMoneyAccount inner) : IReconcileClientMoneyAccount
    {
        public Task<Guid> RunAsync(
            int id,
            ReconciliationTrigger trigger,
            int? ranByUserId,
            CancellationToken cancellationToken) =>
            id == clientMoneyAccountId
                ? throw new InvalidOperationException("Read failed.")
                : inner.RunAsync(id, trigger, ranByUserId, cancellationToken);
    }
}
