using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Infrastructure.Data;

public static class DomainEventDispatcher
{
    public static Task DispatchAsync(
        DbContext context,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mediator);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
