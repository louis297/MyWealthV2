using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyWealthV2.Domain.Common;

namespace MyWealthV2.Infrastructure.Data;

public static class DomainEventDispatcher
{
    public static async Task DispatchAsync(
        DbContext context,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.ChangeTracker.Entries<BaseEntity>().Any(entry => entry.Entity.DomainEvents.Any()))
        {
            return;
        }

        if (services is null)
        {
            return;
        }

        var logger = services.GetService<ILogger<ApplicationDbContext>>()
            ?? NullLogger<ApplicationDbContext>.Instance;
        var mediator = services.GetService<IMediator>();
        if (mediator is null)
        {
            logger.LogError("IMediator is not registered. Domain events were not dispatched.");
            if (services.GetService<IHostEnvironment>()?.IsDevelopment() == true)
            {
                throw new InvalidOperationException(
                    "IMediator is not registered. Domain events were not dispatched.");
            }

            return;
        }

        await DispatchAsync(context, mediator, cancellationToken, logger);
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
