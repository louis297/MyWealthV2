using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;

namespace MyWealthV2.Application.Withdrawals.Commands;

/// <summary>Withdrawals R18–R23: request a withdrawal and reserve its amount.</summary>
[Authorize(Policy = Policies.WithdrawalsRequest)]
public sealed class RequestWithdrawalCommand : IRequest<IdempotentResult>
{
    public Guid? AccountId { get; init; }

    public Guid? NominatedBankAccountId { get; init; }

    public decimal? Amount { get; init; }

    public string? Reference { get; init; }

    public string? Memo { get; init; }

    /// <summary>Refused when present (R1).</summary>
    public Guid? TenantId { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }
}

public class RequestWithdrawalCommandHandler : IRequestHandler<RequestWithdrawalCommand, IdempotentResult>
{
    public Task<IdempotentResult> Handle(RequestWithdrawalCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
