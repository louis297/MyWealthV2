using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyWealthV2.Domain.Common;
using MyWealthV2.Infrastructure.Data;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.Data;

public class DomainEventDispatchTests
{
    [Test]
    public async Task DispatchAsync_PublishesTrackedEventsInOrder_AndClearsThem()
    {
        await using var db = new DispatchContext();
        var entity = new Tracked();
        var first = new Ping("first");
        var second = new Ping("second");
        entity.AddDomainEvent(first);
        entity.AddDomainEvent(second);
        db.Add(entity);

        var mediator = new RecordingMediator();

        await DomainEventDispatcher.DispatchAsync(db, mediator, CancellationToken.None);

        mediator.Published.ShouldBe([first, second]);
        entity.DomainEvents.ShouldBeEmpty();
    }

    [Test]
    public async Task DispatchAsync_LogsHandlerFailure_AndPublishesTheNextEvent()
    {
        await using var db = new DispatchContext();
        var entity = new Tracked();
        var failing = new Ping("fail");
        var next = new Ping("next");
        entity.AddDomainEvent(failing);
        entity.AddDomainEvent(next);
        db.Add(entity);

        var mediator = new RecordingMediator();
        var logger = new RecordingLogger();

        await DomainEventDispatcher.DispatchAsync(db, mediator, CancellationToken.None, logger);

        mediator.Published.ShouldBe([failing, next]);
        entity.DomainEvents.ShouldBeEmpty();
        logger.Errors.Count.ShouldBe(1);
        logger.Errors[0].Exception.ShouldBeOfType<InvalidOperationException>();
        logger.Errors[0].Exception!.Message.ShouldBe("boom");
    }

    [Test]
    public async Task DispatchAsync_LogsMissingMediator_AndThrowsInDevelopment()
    {
        await using var db = new DispatchContext();
        var entity = Track(db, "pending");
        var logger = new RecordingLogger();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            DomainEventDispatcher.DispatchAsync(db, Services(logger, development: true), CancellationToken.None));

        exception.Message.ShouldContain("IMediator");
        logger.Errors.Count.ShouldBe(1);
        logger.Errors[0].Message.ShouldContain("IMediator");
        entity.DomainEvents.Count.ShouldBe(1);
    }

    [Test]
    public async Task DispatchAsync_LogsMissingMediator_AndReturnsOutsideDevelopment()
    {
        await using var db = new DispatchContext();
        var entity = Track(db, "pending");
        var logger = new RecordingLogger();

        await DomainEventDispatcher.DispatchAsync(db, Services(logger, development: false), CancellationToken.None);

        logger.Errors.Count.ShouldBe(1);
        logger.Errors[0].Message.ShouldContain("IMediator");
        entity.DomainEvents.Count.ShouldBe(1);
    }

    [Test]
    public async Task DispatchAsync_DoesNotLog_WhenThereAreNoEvents()
    {
        await using var db = new DispatchContext();
        var logger = new RecordingLogger();

        await DomainEventDispatcher.DispatchAsync(db, Services(logger, development: true), CancellationToken.None);

        logger.Errors.ShouldBeEmpty();
    }

    [Test]
    public async Task DispatchAsync_DoesNotLog_WhenTheContextHasNoServiceProvider()
    {
        await using var db = new DispatchContext();
        var entity = Track(db, "pending");

        await DomainEventDispatcher.DispatchAsync(db, services: null, CancellationToken.None);

        entity.DomainEvents.Count.ShouldBe(1);
    }

    private static Tracked Track(DispatchContext db, string name)
    {
        var entity = new Tracked();
        entity.AddDomainEvent(new Ping(name));
        db.Add(entity);
        return entity;
    }

    private static ServiceProvider Services(RecordingLogger logger, bool development)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<ApplicationDbContext>>(logger);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(development));
        return services.BuildServiceProvider();
    }

    private sealed class DispatchContext : DbContext
    {
        public DispatchContext()
            : base(new DbContextOptionsBuilder<DispatchContext>()
                .UseSqlServer("Server=unused;Database=unused;TrustServerCertificate=True")
                .Options)
        {
        }

        public DbSet<Tracked> Tracked => Set<Tracked>();
    }

    private sealed class Tracked : BaseEntity;

    private sealed class Ping(string name) : BaseEvent
    {
        public string Name { get; } = name;
    }

    private sealed class RecordingMediator : IMediator
    {
        public List<BaseEvent> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Record(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Record(notification!);
            return Task.CompletedTask;
        }

        private void Record(object notification)
        {
            Published.Add((BaseEvent)notification);
            if (notification is Ping { Name: "fail" })
            {
                throw new InvalidOperationException("boom");
            }
        }

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

    private sealed class TestEnvironment(bool development) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            development ? Environments.Development : Environments.Production;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingLogger : ILogger<ApplicationDbContext>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Errors { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error)
            {
                return;
            }

            Errors.Add((logLevel, exception, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
