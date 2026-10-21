using Microsoft.EntityFrameworkCore;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Reports;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class ReportJobTenantScopeTests
{
    [Test]
    public async Task Execute_GeneratorQueryWithoutWhere_SeesOnlyTheExecutionTenant()
    {
        var accessor = new TenantIdAccessor();
        await using var db = OpenDb(accessor);
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var nzd = Currency.Create("NZD", "New Zealand Dollar", 2);
        var instrumentA = Instrument.Create(tenantA.Id, $"AAA{Guid.NewGuid():N}"[..8], "Alpha", nzd);
        var instrumentB = Instrument.Create(tenantB.Id, $"BBB{Guid.NewGuid():N}"[..8], "Beta", nzd);
        instrumentA.CreatedBy = "test";
        instrumentB.CreatedBy = "test";
        db.Instruments.AddRange(instrumentA, instrumentB);
        var report = await db.Reports.SingleAsync(row => row.Code == "adviser-balances");
        var execution = ReportExecution.Queue(tenantA.Id, report.Id, "Manual", "{}", triggeredByUserId: null);
        execution.CreatedBy = "test";
        db.ReportExecutions.Add(execution);
        await db.SaveChangesAsync();

        var generator = new ListingGenerator(db, [instrumentA.PublicId, instrumentB.PublicId])
        {
            Code = report.Code
        };
        var job = new ReportJob(db, new SingleRegistry(generator), new NoFileStore(), new NoScheduler(), TimeProvider.System);

        await job.Execute(execution.PublicId);

        generator.Seen.ShouldBe([instrumentA.PublicId]);
    }

    [Test]
    public async Task RunScheduled_EngagesTheScheduleTenant()
    {
        var accessor = new TenantIdAccessor();
        await using var db = OpenDb(accessor);
        var (tenantA, _) = await TwoTenantFixture.InsertAsync(db);
        var report = await db.Reports.SingleAsync(row => row.Code == "adviser-balances");
        var schedule = ReportSchedule.Create(tenantA.Id, report.Id, "{}", isActive: false, cron: "0 8 * * 1-5");
        schedule.CreatedBy = "test";
        db.ReportSchedules.Add(schedule);
        await db.SaveChangesAsync();

        var job = new ReportJob(db, new SingleRegistry(new UnusedGenerator()), new NoFileStore(), new NoScheduler(), TimeProvider.System);

        await job.RunScheduled(schedule.PublicId);

        accessor.IsEngaged.ShouldBeTrue();
        accessor.TenantId.ShouldBe(tenantA.Id);
    }

    private static ApplicationDbContext OpenDb(TenantIdAccessor accessor)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options, accessor);
    }

    private sealed class ListingGenerator(IApplicationDbContext db, IReadOnlyCollection<Guid> ids) : IReportGenerator
    {
        public string Code { get; init; } = "";

        public List<Guid> Seen { get; } = [];

        public async Task<byte[]> GenerateAsync(ReportExecution execution, CancellationToken cancellationToken)
        {
            Seen.AddRange(await db.Instruments.AsNoTracking()
                .Where(row => ids.Contains(row.PublicId))
                .Select(row => row.PublicId)
                .ToListAsync(cancellationToken));
            return [1];
        }
    }

    private sealed class UnusedGenerator : IReportGenerator
    {
        public string Code => "unused";

        public Task<byte[]> GenerateAsync(ReportExecution execution, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]>([1]);
    }

    private sealed class SingleRegistry(IReportGenerator generator) : IReportGeneratorRegistry
    {
        public IReportGenerator? Find(string code) =>
            string.Equals(generator.Code, code, StringComparison.Ordinal) ? generator : null;
    }

    private sealed class NoFileStore : IReportFileStore
    {
        public Task SaveAsync(string relativeKey, byte[] pdf, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteIfExistsAsync(string relativeKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<byte[]?> ReadAsync(string relativeKey, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>(null);
    }

    private sealed class NoScheduler : IReportScheduler
    {
        public void Enqueue(Guid executionId)
        {
        }

        public void AddOrUpdateRecurring(Guid scheduleId, string cron)
        {
        }

        public void RemoveRecurring(Guid scheduleId)
        {
        }
    }
}
