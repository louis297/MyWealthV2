using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Interest.TaxProfiles;

/// <summary>Interest-run R5: <c>GET /users/customers/{customerId}/tax-profile</c>.</summary>
[Authorize(Policy = Policies.CustomersManage + "," + Policies.CustomersManageOwn)]
public record GetCustomerTaxProfileQuery(Guid CustomerId) : IRequest<TaxProfileDto>;

public class GetCustomerTaxProfileQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCustomerTaxProfileQuery, TaxProfileDto>
{
    public async Task<TaxProfileDto> Handle(GetCustomerTaxProfileQuery request, CancellationToken cancellationToken)
    {
        var customer = await TaxProfileReader.CustomerAsync(db, currentUser, request.CustomerId, cancellationToken);
        return await TaxProfileReader.ItemAsync(db, customer.Id, withUpdatedBy: true, cancellationToken);
    }
}
