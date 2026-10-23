using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MyWealthV2.Infrastructure.Data.Interceptors;

public class DispatchDomainEventsInterceptor : SaveChangesInterceptor
{
    public static Task PublishAfterCommitAsync(DbContext context, IMediator mediator, CancellationToken cancellationToken) =>
        DomainEventDispatcher.DispatchAsync(context, mediator, cancellationToken);
}
