using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;

namespace MyWealthV2.Application.AdjustmentRequests;

/// <summary>Adjustment review R3: a TenantAdmin asks for a correction of the cash, one holding, or both, on one
/// Custodial account. The body is the body of an adjustment (adjustments §8.1).</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public class RequestAdjustmentCommand : IRequest<IdempotentResult>
{
    public Guid? AccountId { get; init; }

    public string? Reason { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    public decimal? Amount { get; init; }

    public AdjustmentSecurity? Security { get; init; }

    /// <summary>Not part of the body (R1).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Not part of the body: the approval books the adjustment at its own clock (R9).</summary>
    public DateTimeOffset? BookedAt { get; init; }

    /// <summary>Not part of the body: the route names the type.</summary>
    public string? Type { get; init; }

    /// <summary>Not part of the body: the Account currency is used.</summary>
    public string? Currency { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }

    /// <summary>The holding part: one instrument and the signed changes.</summary>
    public sealed class AdjustmentSecurity
    {
        public Guid InstrumentId { get; init; }

        public decimal QuantityChange { get; init; }

        public decimal CostChange { get; init; }

        /// <summary>Not part of the body: the instrument's quote currency is used.</summary>
        public string? Currency { get; init; }
    }
}

public class RequestAdjustmentCommandHandler : IRequestHandler<RequestAdjustmentCommand, IdempotentResult>
{
    public Task<IdempotentResult> Handle(RequestAdjustmentCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
