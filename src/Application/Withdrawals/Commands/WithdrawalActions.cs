using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Withdrawals.Commands;

/// <summary>Withdrawals R27–R29, R31: a TenantAdmin other than the requester approves; an outgoing payment waits.</summary>
[Authorize(Policy = Policies.WithdrawalsApprove)]
public sealed class ApproveWithdrawalCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Withdrawals R27, R28: a TenantAdmin other than the requester refuses it, with a note.</summary>
[Authorize(Policy = Policies.WithdrawalsApprove)]
public sealed class RejectWithdrawalCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>Withdrawals R27, R30: withdrawn before the bank was asked to pay it.</summary>
[Authorize(Policy = Policies.WithdrawalsRequest)]
public sealed class CancelWithdrawalCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

public class ApproveWithdrawalCommandHandler : IRequestHandler<ApproveWithdrawalCommand>
{
    public Task Handle(ApproveWithdrawalCommand request, CancellationToken cancellationToken) => throw new NotImplementedException();
}

public class RejectWithdrawalCommandHandler : IRequestHandler<RejectWithdrawalCommand>
{
    public Task Handle(RejectWithdrawalCommand request, CancellationToken cancellationToken) => throw new NotImplementedException();
}

public class CancelWithdrawalCommandHandler : IRequestHandler<CancelWithdrawalCommand>
{
    public Task Handle(CancelWithdrawalCommand request, CancellationToken cancellationToken) => throw new NotImplementedException();
}
