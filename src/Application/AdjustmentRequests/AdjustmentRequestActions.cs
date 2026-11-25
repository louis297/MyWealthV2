using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.AdjustmentRequests;

/// <summary>Adjustment review R7: the body of every decision on an adjustment request.</summary>
public abstract class AdjustmentRequestActionCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public Guid? TenantId { get; init; }
}

/// <summary>R8, R9: a TenantAdmin other than the requester approves the request, and the adjustment is posted.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed class ApproveAdjustmentRequestCommand : AdjustmentRequestActionCommand;

/// <summary>R8, R10: a TenantAdmin other than the requester refuses the request, with a note.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed class RejectAdjustmentRequestCommand : AdjustmentRequestActionCommand;

/// <summary>R11: any TenantAdmin withdraws the request, the requester included.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed class CancelAdjustmentRequestCommand : AdjustmentRequestActionCommand;

public class ApproveAdjustmentRequestCommandHandler : IRequestHandler<ApproveAdjustmentRequestCommand>
{
    public Task Handle(ApproveAdjustmentRequestCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class RejectAdjustmentRequestCommandHandler : IRequestHandler<RejectAdjustmentRequestCommand>
{
    public Task Handle(RejectAdjustmentRequestCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class CancelAdjustmentRequestCommandHandler : IRequestHandler<CancelAdjustmentRequestCommand>
{
    public Task Handle(CancelAdjustmentRequestCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
