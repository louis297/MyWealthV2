using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.NetWorth.Queries.GetNetWorth;

[Authorize(Policy = Policies.AccountsRead)]
public record GetNetWorthQuery(Guid? TenantId = null, Guid? CustomerId = null) : IRequest<NetWorthDto>;

public class GetNetWorthQueryValidator : AbstractValidator<GetNetWorthQuery>
{
    public GetNetWorthQueryValidator(ICurrentUser currentUser)
    {
        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId)
                .NotEmpty()
                .WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

public class GetNetWorthQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IMarketData marketData,
    TimeProvider time) : IRequestHandler<GetNetWorthQuery, NetWorthDto>
{
    public async Task<NetWorthDto> Handle(GetNetWorthQuery request, CancellationToken cancellationToken)
    {
        int tenantId;
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            var tenant = await db.Tenants.AsNoTracking()
                             .SingleOrDefaultAsync(row => row.PublicId == request.TenantId, cancellationToken)
                         ?? throw new NotFoundException("Tenant", request.TenantId!);
            tenantId = tenant.Id;
        }
        else
        {
            tenantId = currentUser.TenantId ?? throw new NotFoundException("Tenant", "current");
        }

        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        int? customerId = null;
        if (request.CustomerId is Guid customerPublicId)
        {
            var customers = db.Users.AsNoTracking()
                .Where(user => user.PublicId == customerPublicId
                               && user.Role == UserRole.Customer
                               && user.TenantId == tenantId);
            if (assignedAdviserId is not null)
            {
                customers = customers.Where(user => user.AdviserId == assignedAdviserId);
            }

            var customer = await customers.SingleOrDefaultAsync(cancellationToken)
                           ?? throw new NotFoundException("Customer", customerPublicId);
            customerId = customer.Id;
        }

        return await NetWorthReader.ReadAsync(
            db, marketData, time, tenantId, customerId, assignedAdviserId, cancellationToken);
    }
}
