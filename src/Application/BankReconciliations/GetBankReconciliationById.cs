using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>R21: one run of the current tenant with its items.</summary>
[Authorize(Policy = Policies.BankReconciliationsManage)]
public sealed record GetBankReconciliationByIdQuery(Guid Id) : IRequest<BankReconciliationDto>;

public sealed class GetBankReconciliationByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetBankReconciliationByIdQuery, BankReconciliationDto>
{
    public async Task<BankReconciliationDto> Handle(GetBankReconciliationByIdQuery request, CancellationToken cancellationToken)
    {
        var tenantId = BankReconciliationReader.CurrentTenantId(currentUser);
        var run = await db.BankReconciliations.AsNoTracking()
                      .SingleOrDefaultAsync(row => row.PublicId == request.Id && row.TenantId == tenantId, cancellationToken)
                  ?? throw new NotFoundException("Bank reconciliation", request.Id);
        return await BankReconciliationReader.ToDtoAsync(db, run, cancellationToken);
    }
}
