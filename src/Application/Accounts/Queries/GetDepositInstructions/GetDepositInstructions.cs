using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Accounts.Queries.GetDepositInstructions;

/// <summary>
/// Client money accounts R20–R22: where to pay in to an Open Custodial account. 404 when the caller cannot see the
/// account, or when it is External, Closed, or its Tenant has no active client money account in its currency.
/// </summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetDepositInstructionsQuery(Guid AccountId) : IRequest<DepositInstructionsDto>;

/// <summary>Deposit instructions body (client-money-accounts R22).</summary>
public sealed record DepositInstructionsDto(
    Guid AccountId,
    string Currency,
    string Reference,
    string BankName,
    string AccountName,
    string AccountNumber);

public class GetDepositInstructionsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetDepositInstructionsQuery, DepositInstructionsDto>
{
    public async Task<DepositInstructionsDto> Handle(GetDepositInstructionsQuery request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser).AsNoTracking()
                          .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
                      ?? throw new NotFoundException("Account", request.AccountId);

        if (account.CustodyMode != CustodyMode.Custodial
            || account.Status != AccountStatus.Open
            || account.DepositReference is null)
        {
            throw new NotFoundException("Deposit instructions", request.AccountId);
        }

        var clientMoneyAccount = await db.ClientMoneyAccounts.AsNoTracking()
                                     .SingleOrDefaultAsync(
                                         row => row.TenantId == account.TenantId
                                                && row.Currency == account.Currency
                                                && row.IsActive,
                                         cancellationToken)
                                 ?? throw new NotFoundException("Deposit instructions", request.AccountId);

        return new DepositInstructionsDto(
            account.PublicId,
            account.Currency,
            account.DepositReference.Value,
            clientMoneyAccount.BankName,
            clientMoneyAccount.AccountName,
            clientMoneyAccount.AccountNumber);
    }
}
