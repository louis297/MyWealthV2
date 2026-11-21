using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.BankReconciliations;

/// <summary>R21: one run of the current tenant with its items.</summary>
[Authorize(Policy = Policies.BankReconciliationsManage)]
public sealed record GetBankReconciliationByIdQuery(Guid Id) : IRequest<BankReconciliationDto>;

public sealed class GetBankReconciliationByIdQueryHandler : IRequestHandler<GetBankReconciliationByIdQuery, BankReconciliationDto>
{
    public Task<BankReconciliationDto> Handle(GetBankReconciliationByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
