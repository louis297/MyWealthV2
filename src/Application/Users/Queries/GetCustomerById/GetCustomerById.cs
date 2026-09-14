using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Users.Queries.GetCustomerById;

[Authorize(Policy = Policies.CustomersManage + "," + Policies.CustomersManageOwn)]
public record GetCustomerByIdQuery(Guid Id) : IRequest<CustomerDto>;

public class GetCustomerByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCustomerByIdQuery, CustomerDto>
{
    public async Task<CustomerDto> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId;
        var assignedAdviserId = CustomerScope.AssignedAdviserInternalId(currentUser);

        var row = await (
                      from user in db.Users.AsNoTracking()
                      join tenant in db.Tenants.AsNoTracking() on user.TenantId equals tenant.Id
                      join adviser in db.Users.AsNoTracking() on user.AdviserId equals adviser.Id
                      where user.PublicId == request.Id
                            && user.Role == UserRole.Customer
                            && user.TenantId == tenantId
                            && (assignedAdviserId == null || user.AdviserId == assignedAdviserId)
                      select new { user, tenant, adviser }
                  ).SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("Customer", request.Id);

        return CustomerDto.From(row.user, row.tenant, row.adviser);
    }
}
