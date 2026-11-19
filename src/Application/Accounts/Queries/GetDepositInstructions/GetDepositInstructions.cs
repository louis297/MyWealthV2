using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Accounts.Queries.GetDepositInstructions;

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

public class GetDepositInstructionsQueryHandler
    : IRequestHandler<GetDepositInstructionsQuery, DepositInstructionsDto>
{
    public Task<DepositInstructionsDto> Handle(GetDepositInstructionsQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
