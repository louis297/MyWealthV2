using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Common.Behaviours;

public class TenantScopeBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantIdAccessor _tenantIds;

    public TenantScopeBehaviour(ICurrentUser currentUser, ITenantIdAccessor tenantIds)
    {
        _currentUser = currentUser;
        _tenantIds = tenantIds;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_currentUser.TenantId is int tenantId)
        {
            _tenantIds.Engage(tenantId);
        }

        return await next(cancellationToken);
    }
}
