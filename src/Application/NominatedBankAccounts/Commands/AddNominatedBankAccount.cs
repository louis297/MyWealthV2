using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.NominatedBankAccounts.Commands;

/// <summary>Withdrawals R4: add a Customer's own bank account, Pending until a TenantAdmin checks it.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsManage)]
public sealed class AddNominatedBankAccountCommand : IRequest<Guid>
{
    public Guid? CustomerId { get; init; }

    public string? Currency { get; init; }

    public string? BankName { get; init; }

    public string? AccountName { get; init; }

    public string? AccountNumber { get; init; }

    /// <summary>Refused when present (R4).</summary>
    public string? Status { get; init; }

    /// <summary>Refused when present (R1, R4).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Refused when present (R4).</summary>
    public Guid? Id { get; init; }
}

public class AddNominatedBankAccountCommandHandler : IRequestHandler<AddNominatedBankAccountCommand, Guid>
{
    public Task<Guid> Handle(AddNominatedBankAccountCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
