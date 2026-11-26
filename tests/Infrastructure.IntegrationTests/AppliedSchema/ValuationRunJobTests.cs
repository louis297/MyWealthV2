using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Valuations.Runs;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Accounts;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.Valuations;
using MyWealthV2.Shared;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>
/// Valuation-run R14: the daily recurring job <c>valuation-run</c>. The schema test database is shared, so the run of a
/// Tenant that is not part of a test's world is recorded and not executed.
/// </summary>
public class ValuationRunJobTests
{
    private static DateOnly LastFinalDay => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);

    [Test]
    public void Register_AddsADailyUtcJob_At1300_OnQueueValuation()
    {
        using var host = HangfireHost();

        ValuationRunRecurringJob.Register(host.Services.GetRequiredService<IRecurringJobManager>());

        using var storage = host.Services.GetRequiredService<JobStorage>().GetConnection();
        storage.GetAllItemsFromSet("recurring-jobs").ShouldBe(["valuation-run"]);
        var job = storage.GetAllEntriesFromHash("recurring-job:valuation-run");
        job["Cron"].ShouldBe("0 13 * * *");
        job["TimeZoneId"].ShouldBe("UTC");
        using var payload = JsonDocument.Parse(job["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("valuation");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("ValuationRunJob");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Run");
    }

    [Test]
    public void Run_HasNoAutomaticRetry()
    {
        var retry = typeof(ValuationRunJob).GetMethod(nameof(ValuationRunJob.Run))!
            .CustomAttributes
            .Single(data => data.AttributeType == typeof(AutomaticRetryAttribute));

        retry.NamedArguments.Single(argument => argument.MemberName == nameof(AutomaticRetryAttribute.Attempts))
            .TypedValue.Value.ShouldBe(0);
    }

    [Test]
    public async Task Run_RunsEveryTenantWithACustodialAccount_ThroughTheLastFinalDay_ADisabledOneIncluded()
    {
        var world = await InsertWorldAsync();
        var calls = new List<int>();
        using var host = WorkerHost(world, calls);

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ValuationRunJob>().Run(CancellationToken.None);
        }

        calls.Where(world.All.Contains).ShouldBe([world.Custodial, world.Disabled]);
        await using var db = OpenDb();
        var runs = await db.ValuationRuns.AsNoTracking().Where(row => world.All.Contains(row.TenantId)).ToListAsync();
        runs.Select(row => row.TenantId).OrderBy(id => id).ShouldBe([world.Custodial, world.Disabled]);
        runs.ShouldAllBe(row => row.Trigger == ValuationRunTrigger.Scheduled && row.RanByUserId == null
                                && row.ThroughDate == LastFinalDay && row.CompletedAt != null
                                && row.CreatedBy == SystemActor.Name);
    }

    [Test]
    public async Task Run_WithOneTenantFailing_RunsTheNext_AndThenFails()
    {
        var world = await InsertWorldAsync();
        using var host = WorkerHost(world, [], failTenant: world.Custodial);

        using (var scope = host.Services.CreateScope())
        {
            var exception = await Should.ThrowAsync<ValuationRunFailedException>(() =>
                scope.ServiceProvider.GetRequiredService<ValuationRunJob>().Run(CancellationToken.None));
            exception.Message.ShouldContain(world.CustodialPublicId.ToString());
        }

        await using var db = OpenDb();
        (await db.ValuationRuns.AsNoTracking().Where(row => world.All.Contains(row.TenantId)).Select(row => row.TenantId).ToListAsync())
            .ShouldBe([world.Disabled]);
    }

    [Test]
    public async Task Run_WithAnAccountFailing_IsReportedAsFailed_AfterEveryTenantWasTried()
    {
        var world = await InsertWorldAsync();
        using var host = WorkerHost(world, [], accountFailureTenant: world.Custodial);

        using (var scope = host.Services.CreateScope())
        {
            var exception = await Should.ThrowAsync<ValuationRunFailedException>(() =>
                scope.ServiceProvider.GetRequiredService<ValuationRunJob>().Run(CancellationToken.None));
            exception.Message.ShouldContain(world.CustodialPublicId.ToString());
        }

        await using var db = OpenDb();
        (await db.ValuationRuns.AsNoTracking().Where(row => world.All.Contains(row.TenantId)).Select(row => row.TenantId).ToListAsync())
            .ShouldBe([world.Disabled]);
    }

    private sealed record World(int Custodial, Guid CustodialPublicId, int Disabled, int ExternalOnly)
    {
        public int[] All => [Custodial, Disabled, ExternalOnly];
    }

    /// <summary>Tenant A and disabled tenant B with a Custodial account opened today; tenant C with an External account
    /// only. A has the lowest Id.</summary>
    private static async Task<World> InsertWorldAsync()
    {
        await using var db = OpenDb();
        var (a, b) = await TwoTenantFixture.InsertAsync(db);
        var (c, d) = await TwoTenantFixture.InsertAsync(db);
        var (adviserA, adviserB) = await TwoTenantFixture.InsertAdvisersAsync(db, a, b);
        var (customerA, customerB) = await TwoTenantFixture.InsertCustomersAsync(db, a, b, adviserA, adviserB);
        var (adviserC, adviserD) = await TwoTenantFixture.InsertAdvisersAsync(db, c, d);
        var (customerC, _) = await TwoTenantFixture.InsertCustomersAsync(db, c, d, adviserC, adviserD);
        var nzd = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == "NZD");
        foreach (var (tenant, customer, mode) in new[]
                 {
                     (a, customerA, CustodyMode.Custodial), (b, customerB, CustodyMode.Custodial), (c, customerC, CustodyMode.External)
                 })
        {
            var account = Account.Create(tenant.Id, customer.Id, "Managed", AccountType.Brokerage, mode, nzd,
                mode == CustodyMode.Custodial ? DepositReference.Generate(CryptoRandomSymbols.Instance) : null);
            account.Created = DateTimeOffset.UtcNow;
            account.CreatedBy = "test";
            db.Accounts.Add(account);
        }

        (await db.Tenants.SingleAsync(row => row.Id == b.Id)).Disable();
        await db.SaveChangesAsync();
        return new World(a.Id, a.PublicId, b.Id, c.Id);
    }

    private static IHost HangfireHost()
    {
        var builder = TestHost.CreateBuilder();
        builder.Services.AddHangfire(configuration => configuration.UseInMemoryStorage());
        return builder.Build();
    }

    private static IHost WorkerHost(World world, List<int> calls, int? failTenant = null, int? accountFailureTenant = null)
    {
        var builder = TestHost.CreateBuilder();
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] = SchemaDatabaseSetup.ConnectionString;
        builder.AddInfrastructureServices();
        builder.Services.AddScoped<IUser, SystemUser>();
        builder.Services.RemoveAll<IRunValuations>();
        builder.Services.AddScoped<RunValuations>();
        builder.Services.AddScoped<IRunValuations>(services => new WorldOnly(
            world.All.ToHashSet(), calls, failTenant, accountFailureTenant, services.GetRequiredService<RunValuations>()));
        return builder.Build();
    }

    private static ApplicationDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(SchemaDatabaseSetup.ConnectionString).Options);

    /// <summary>Runs the Tenants of the world and records every call; throws for one Tenant, or answers an unsaved run
    /// with a failed account for another. The run of any other Tenant is answered unsaved and not executed.</summary>
    private sealed class WorldOnly(
        HashSet<int> world, List<int> calls, int? failTenant, int? accountFailureTenant, IRunValuations inner) : IRunValuations
    {
        public Task<ValuationRun> RunAsync(
            int tenantId,
            DateOnly throughDate,
            ValuationRunTrigger trigger,
            int? ranByUserId,
            CancellationToken cancellationToken)
        {
            lock (calls)
            {
                calls.Add(tenantId);
            }

            if (tenantId == failTenant)
            {
                throw new InvalidOperationException("Run failed.");
            }

            if (tenantId == accountFailureTenant || !world.Contains(tenantId))
            {
                var run = ValuationRun.Start(tenantId, throughDate, trigger, ranByUserId, DateTimeOffset.UtcNow);
                var failed = tenantId == accountFailureTenant ? 1 : 0;
                run.Complete(failed, failed, 0, 0, DateTimeOffset.UtcNow);
                return Task.FromResult(run);
            }

            return inner.RunAsync(tenantId, throughDate, trigger, ranByUserId, cancellationToken);
        }
    }
}
