using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MyWealthV2.Domain.Common;

namespace MyWealthV2.Infrastructure.Data;

public static class DomainEventDispatcher
{
    public static Task DispatchAsync(
        DbContext context,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ = services;
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException();
    }

    public static async Task DispatchAsync(
        DbContext context,
        IMediator mediator,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mediator);

        var entities = context.ChangeTracker
            .Entries<BaseEntity>()
            .Where(entry => entry.Entity.DomainEvents.Any())
            .Select(entry => entry.Entity)
            .ToList();

        var domainEvents = entities.SelectMany(entity => entity.DomainEvents).ToList();
        entities.ForEach(entity => entity.ClearDomainEvents());

        foreach (var domainEvent in domainEvents)
        {
            try
            {
                await mediator.Publish(domainEvent, cancellationToken);
            }
            catch (Exception exception)
            {
                logger?.LogError(
                    exception,
                    "Domain event {EventType} failed after save. The saved change stays committed.",
                    domainEvent.GetType().Name);
            }
        }
    }
}
