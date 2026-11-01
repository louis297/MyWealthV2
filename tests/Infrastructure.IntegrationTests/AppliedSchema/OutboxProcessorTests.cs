using System.Text.Json;
using Hangfire;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Common;
using MyWealthV2.Domain.Events;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Data.Entities;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class OutboxProcessorTests
{
    [Test]
    public async Task Process_PublishesPending_AndMarksProcessed()
    {
        var domainEvent = new CustomerAdviserReassigned(11, 22, 33, 44);
        var log = new PublishLog();
        await using var db = await OpenDbAsync();
        var id = await InsertAsync(db, domainEvent, tenantId: null, occurredAt: DateTimeOffset.UtcNow);

        await ProcessAsync(log, TimeProvider.System);

        var row = await ReloadAsync(db, id);
        row.Status.ShouldBe("Processed");
        row.ProcessedAt.ShouldNotBeNull();
        row.Error.ShouldBeNull();
        var published = log.Entries.ShouldHaveSingleItem();
        published.Engaged.ShouldBeFalse();
        var reassigned = published.Event.ShouldBeOfType<CustomerAdviserReassigned>();
        reassigned.CustomerId.ShouldBe(11);
        reassigned.NewAdviserId.ShouldBe(44);
    }

    [Test]
    public async Task Process_FailingPublish_FailsThenDeadLettersAtFive()
    {
        var log = new PublishLog { Failure = new InvalidOperationException("publish failed") };
        await using var db = await OpenDbAsync();
        var id = await InsertAsync(db, new CustomerAdviserReassigned(1, 2, 3, 4), tenantId: null, occurredAt: DateTimeOffset.UtcNow);

        await ProcessAsync(log, TimeProvider.System);
        var failed = await ReloadAsync(db, id);
        failed.Status.ShouldBe("Failed");
        failed.RetryCount.ShouldBe(1);
        failed.Error.ShouldBe("publish failed");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await ProcessAsync(log, TimeProvider.System);
        }

        var stillFailed = await ReloadAsync(db, id);
        stillFailed.Status.ShouldBe("Failed");
        stillFailed.RetryCount.ShouldBe(4);

        await ProcessAsync(log, TimeProvider.System);
        var dead = await ReloadAsync(db, id);
        dead.Status.ShouldBe("DeadLettered");
        dead.RetryCount.ShouldBe(5);

        await ProcessAsync(log, TimeProvider.System);
        log.Entries.Count.ShouldBe(5);
        (await ReloadAsync(db, id)).Status.ShouldBe("DeadLettered");
    }

    [Test]
    public async Task Process_EngagesTenantFromTheRow_AndLeavesPlatformRowsDisengaged()
    {
        var log = new PublishLog();
        await using var db = await OpenDbAsync();
        var tenantScoped = await InsertAsync(
            db, new CustomerAdviserReassigned(1, 70, 2, 3), tenantId: 70, occurredAt: DateTimeOffset.UtcNow);
        var platform = await InsertAsync(
            db, new CustomerAdviserReassigned(9, 1, 2, 3), tenantId: null, occurredAt: DateTimeOffset.UtcNow.AddMinutes(1));

        await ProcessAsync(log, TimeProvider.System);

        log.Entries.Count.ShouldBe(2);
        log.Entries[0].Engaged.ShouldBeTrue();
        log.Entries[0].TenantId.ShouldBe(70);
        log.Entries[1].Engaged.ShouldBeFalse();
        log.Entries[1].TenantId.ShouldBeNull();
        (await ReloadAsync(db, tenantScoped)).Status.ShouldBe("Processed");
        (await ReloadAsync(db, platform)).Status.ShouldBe("Processed");
    }

    [Test]
    public async Task Process_ReclaimsStaleProcessing_AndSkipsAFreshClaim()
    {
        var log = new PublishLog();
        var now = DateTimeOffset.UtcNow;
        await using var db = await OpenDbAsync();
        var stale = await InsertAsync(
            db,
            new CustomerAdviserReassigned(1, 2, 3, 4),
            tenantId: null,
            occurredAt: now.AddMinutes(-10),
            status: "Processing",
            lastModified: now.AddMinutes(-6));
        var fresh = await InsertAsync(
            db,
            new CustomerAdviserReassigned(5, 6, 7, 8),
            tenantId: null,
            occurredAt: now.AddMinutes(-9),
            status: "Processing",
            lastModified: now);

        await ProcessAsync(log, TimeProvider.System);

        var reclaimed = await ReloadAsync(db, stale);
        reclaimed.Status.ShouldBe("Processed");
        reclaimed.RetryCount.ShouldBe(1);
        (await ReloadAsync(db, fresh)).Status.ShouldBe("Processing");
        log.Entries.Count.ShouldBe(1);
    }

    [Test]
    public async Task Process_PublishesTheOldestTwenty_AndLeavesTheNewestPending()
    {
        var log = new PublishLog();
        var now = DateTimeOffset.UtcNow;
        await using var db = await OpenDbAsync();
        var ids = new List<Guid>();
        for (var index = 0; index < 21; index++)
        {
            ids.Add(await InsertAsync(
                db,
                new CustomerAdviserReassigned(index, 1, 2, 3),
                tenantId: null,
                occurredAt: now.AddSeconds(index)));
        }

        await ProcessAsync(log, TimeProvider.System);

        log.Entries.Count.ShouldBe(20);
        for (var index = 0; index < 20; index++)
        {
            (await ReloadAsync(db, ids[index])).Status.ShouldBe("Processed");
        }

        (await ReloadAsync(db, ids[20])).Status.ShouldBe("Pending");
    }

    private static async Task ProcessAsync(PublishLog log, TimeProvider time)
    {
        var services = new ServiceCollection();
        services.AddSingleton(time);
        services.AddSingleton(log);
        services.AddScoped<ITenantIdAccessor, TenantIdAccessor>();
        services.AddScoped<IMediator, RecordingMediator>();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(SchemaDatabaseSetup.ConnectionString));
        services.AddScoped<OutboxProcessor>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().Process();
    }

    private static async Task<ApplicationDbContext> OpenDbAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        var db = new ApplicationDbContext(options);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [OutboxMessages]");
        return db;
    }

    private static async Task<Guid> InsertAsync(
        ApplicationDbContext db,
        CustomerAdviserReassigned domainEvent,
        int? tenantId,
        DateTimeOffset occurredAt,
        string status = "Pending",
        DateTimeOffset? lastModified = null)
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = typeof(CustomerAdviserReassigned).FullName!,
            Payload = OutboxSerializer.Serialize(domainEvent),
            OccurredAt = occurredAt,
            TenantId = tenantId,
            Status = status,
            RetryCount = 0,
            Created = occurredAt,
            CreatedBy = "test",
            LastModified = lastModified
        };
        db.OutboxMessages.Add(message);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return message.Id;
    }

    private static Task<OutboxMessage> ReloadAsync(ApplicationDbContext db, Guid id) =>
        db.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == id);

    private sealed class PublishLog
    {
        public List<(bool Engaged, int? TenantId, BaseEvent Event)> Entries { get; } = [];

        public Exception? Failure { get; init; }
    }

    private sealed class RecordingMediator(ITenantIdAccessor tenants, PublishLog log) : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            log.Entries.Add((tenants.IsEngaged, tenants.TenantId, (BaseEvent)notification));
            if (log.Failure is not null)
            {
                throw log.Failure;
            }

            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Publish((object)notification!, cancellationToken);

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
