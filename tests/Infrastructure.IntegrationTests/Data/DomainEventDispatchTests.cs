using MediatR;
using Microsoft.EntityFrameworkCore;
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
            Published.Add((BaseEvent)notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add((BaseEvent)(object)notification!);
            return Task.CompletedTask;
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
}
