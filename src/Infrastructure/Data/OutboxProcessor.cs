using Microsoft.Extensions.DependencyInjection;

namespace MyWealthV2.Infrastructure.Data;

public sealed class OutboxProcessor(IServiceScopeFactory scopes, TimeProvider time)
{
    public Task Process()
    {
        _ = scopes;
        _ = time;
        return Task.CompletedTask;
    }
}
