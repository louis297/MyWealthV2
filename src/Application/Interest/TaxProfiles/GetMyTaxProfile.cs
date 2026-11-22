using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Interest.TaxProfiles;

/// <summary>Interest-run R5, R8: <c>GET /users/me/tax-profile</c>, the caller's own profile when the caller is a
/// Customer, without <c>updatedBy</c>; any other role → 404.</summary>
[Authorize(Policy = Policies.UsersMe)]
public record GetMyTaxProfileQuery : IRequest<TaxProfileDto>;

public class GetMyTaxProfileQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyTaxProfileQuery, TaxProfileDto>
{
    public async Task<TaxProfileDto> Handle(GetMyTaxProfileQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.Role != UserRole.Customer || currentUser.Person is not { } person)
        {
            throw new NotFoundException("Tax profile", currentUser.PublicId ?? Guid.Empty);
        }

        return await TaxProfileReader.ItemAsync(db, person.Id, withUpdatedBy: false, cancellationToken);
    }
}
