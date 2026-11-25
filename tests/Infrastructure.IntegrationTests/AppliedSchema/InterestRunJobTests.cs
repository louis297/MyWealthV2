using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Interest.Runs;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using MyWealthV2.Infrastructure.Interest;
using MyWealthV2.Shared;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

/// <summary>Interest-run R31: the daily recurring job <c>interest-run</c>.</summary>
public class InterestRunJobTests
{
    private static DateOnly Yesterday => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

    [Test]
    public void Register_AddsADailyUtcJob_At0030_OnQueueInterest()
    {
        using var host = HangfireHost();

        InterestRunRecurringJob.Register(host.Services.GetRequiredService<IRecurringJobManager>());

        using var storage = host.Services.GetRequiredService<JobStorage>().GetConnection();
        storage.GetAllItemsFromSet("recurring-jobs").ShouldBe(["interest-run"]);
        var job = storage.GetAllEntriesFromHash("recurring-job:interest-run");
        job["Cron"].ShouldBe("30 0 * * *");
        job["TimeZoneId"].ShouldBe("UTC");
        using var payload = JsonDocument.Parse(job["Job"]);
        payload.RootElement.GetProperty("Queue").GetString().ShouldBe("interest");
        payload.RootElement.GetProperty("Type").GetString()!.ShouldContain("InterestRunJob");
        payload.RootElement.GetProperty("Method").GetString().ShouldBe("Run");
    }

    [Test]
    public void Run_HasNoAutomaticRetry()
    {
        var retry = typeof(InterestRunJob).GetMethod(nameof(InterestRunJob.Run))!
            .CustomAttributes
            .Single(data => data.AttributeType == typeof(AutomaticRetryAttribute));

        retry.NamedArguments.Single(argument => argument.MemberName == nameof(AutomaticRetryAttribute.Attempts))
            .TypedValue.Value.ShouldBe(0);
    }

    [Test]
    public async Task Run_RunsEveryTenantWithASchedule_ForYesterday_ADisabledOneIncluded_AndSkipsATenantWithout()
    {
        var world = await InsertWorldAsync();
        using var host = WorkerHost();

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<InterestRunJob>().Run(CancellationToken.None);
        }

        await using var db = OpenDb();
        var runs = await db.InterestRuns.AsNoTracking().Where(row => world.All.Contains(row.TenantId)).ToListAsync();
        runs.Select(row => row.TenantId).OrderBy(id => id).ShouldBe([world.WithSchedule, world.Disabled]);
        runs.ShouldAllBe(row => row.Trigger == InterestRunTrigger.Scheduled && row.RanByUserId == null
                                && row.AsOfDate == Yesterday && row.CompletedAt != null && row.CreatedBy == SystemActor.Name);
    }

    [Test]
    public async Task Run_WithOneTenantFailing_RunsTheNext_AndThenFails()
    {
        var world = await InsertWorldAsync();
        using var host = WorkerHost(failTenant: world.WithSchedule);

        using (var scope = host.Services.CreateScope())
        {
            var exception = await Should.ThrowAsync<InterestRunFailedException>(() =>
                scope.ServiceProvider.GetRequiredService<InterestRunJob>().Run(CancellationToken.None));
            exception.Message.ShouldContain(world.WithSchedulePublicId.ToString());
        }

        await using var db = OpenDb();
        (await db.InterestRuns.AsNoTracking().Where(row => world.All.Contains(row.TenantId)).Select(row => row.TenantId).ToListAsync())
            .ShouldBe([world.Disabled]);
    }

    [Test]
    public async Task Run_WithAnAccountFailing_IsReportedAsFailed_AfterEveryTenantWasTried()
    {
        var world = await InsertWorldAsync();
        using var host = WorkerHost(accountFailureTenant: world.WithSchedule);

        using (var scope = host.Services.CreateScope())
        {
            var exception = await Should.ThrowAsync<InterestRunFailedException>(() =>
                scope.ServiceProvider.GetRequiredService<InterestRunJob>().Run(CancellationToken.None));
            exception.Message.ShouldContain(world.WithSchedulePublicId.ToString());
        }

        await using var db = OpenDb();
        (await db.InterestRuns.AsNoTracking().Where(row => world.All.Contains(row.TenantId)).Select(row => row.TenantId).ToListAsync())
            .ShouldBe([world.Disabled]);
    }

    private sealed record World(int WithSchedule, Guid WithSchedulePublicId, int Disabled, int WithoutSchedule)
    {
        public int[] All => [WithSchedule, Disabled, WithoutSchedule];
    }

    /// <summary>Tenant A and disabled tenant B with a USD schedule; tenant C without one. A has the lowest Id.</summary>
    private static async Task<World> InsertWorldAsync()
    {
        await using var db = OpenDb();
        var (a, b) = await TwoTenantFixture.InsertAsync(db);
        var (c, _) = await TwoTenantFixture.InsertAsync(db);
        var usd = await db.Set<Currency>().AsNoTracking().SingleAsync(row => row.Code == "USD");
        foreach (var tenant in new[] { a, b })
        {
            var schedule = InterestRateSchedule.Create(tenant.Id, usd, new DateOnly(2026, 11, 1), [new FeeBand(0m, 3m)], new DateOnly(2026, 10, 31));
            schedule.CreatedBy = "test";
            db.InterestRateSchedules.Add(schedule);
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

    private static IHost WorkerHost(int? failTenant = null, int? accountFailureTenant = null)
    {
        var builder = TestHost.CreateBuilder();
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] = SchemaDatabaseSetup.ConnectionString;
        builder.AddInfrastructureServices();
        builder.Services.AddScoped<IUser, SystemUser>();
        if (failTenant is not null || accountFailureTenant is not null)
        {
            builder.Services.RemoveAll<IRunInterest>();
            builder.Services.AddScoped<RunInterest>();
            builder.Services.AddScoped<IRunInterest>(sp =>
                new FailingFor(failTenant, accountFailureTenant, sp.GetRequiredService<RunInterest>()));
        }

        return builder.Build();
    }

    private static ApplicationDbContext OpenDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(SchemaDatabaseSetup.ConnectionString).Options);

    /// <summary>Throws for one tenant, or answers an unsaved run with a failed account for another; runs the rest.</summary>
    private sealed class FailingFor(int? failTenant, int? accountFailureTenant, IRunInterest inner) : IRunInterest
    {
        public Task<InterestRun> RunAsync(
            int tenantId,
            DateOnly asOfDate,
            InterestRunTrigger trigger,
            int? ranByUserId,
            CancellationToken cancellationToken)
        {
            if (tenantId == failTenant)
            {
                throw new InvalidOperationException("Run failed.");
            }

            if (tenantId == accountFailureTenant)
            {
                var run = InterestRun.Start(tenantId, asOfDate, trigger, ranByUserId, null, DateTimeOffset.UtcNow);
                run.Complete(1, 1, 0, DateTimeOffset.UtcNow);
                return Task.FromResult(run);
            }

            return inner.RunAsync(tenantId, asOfDate, trigger, ranByUserId, cancellationToken);
        }
    }
}
