using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.NominatedBankAccounts.Commands;

/// <summary>Withdrawals R7–R11: a TenantAdmin other than the adder checked the evidence.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsVerify)]
public sealed class VerifyNominatedBankAccountCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Withdrawals R7–R11: a TenantAdmin other than the adder refuses it.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsVerify)]
public sealed class RejectNominatedBankAccountCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Withdrawals R7, R8, R11, R14: no longer used.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsManage)]
public sealed class RemoveNominatedBankAccountCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

public class VerifyNominatedBankAccountCommandHandler : IRequestHandler<VerifyNominatedBankAccountCommand>
{
    public Task Handle(VerifyNominatedBankAccountCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class RejectNominatedBankAccountCommandHandler : IRequestHandler<RejectNominatedBankAccountCommand>
{
    public Task Handle(RejectNominatedBankAccountCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class RemoveNominatedBankAccountCommandHandler : IRequestHandler<RemoveNominatedBankAccountCommand>
{
    public Task Handle(RemoveNominatedBankAccountCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
