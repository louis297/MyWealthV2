using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Transactions.Commands.CreateAdjustment;

/// <summary>Adjustments §7: a TenantAdmin's correction of the cash, one holding, or both, on one Custodial account.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public class CreateAdjustmentCommand : IRequest<IdempotentResult>
{
    public Guid? AccountId { get; init; }

    public string? Reason { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    public decimal? Amount { get; init; }

    public AdjustmentSecurity? Security { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Not part of the body (R14): the server books an adjustment.</summary>
    public DateTimeOffset? BookedAt { get; init; }

    /// <summary>Not part of the body: the route names the type.</summary>
    public string? Type { get; init; }

    /// <summary>Not part of the body (R9): the Account currency is used.</summary>
    public string? Currency { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }

    /// <summary>The holding part (R10): one instrument and the signed changes.</summary>
    public sealed class AdjustmentSecurity
    {
        public Guid InstrumentId { get; init; }

        public decimal QuantityChange { get; init; }

        public decimal CostChange { get; init; }

        /// <summary>Not part of the body (R10): the instrument's quote currency is used.</summary>
        public string? Currency { get; init; }
    }
}

public class CreateAdjustmentCommandHandler : IRequestHandler<CreateAdjustmentCommand, IdempotentResult>
{
    public Task<IdempotentResult> Handle(CreateAdjustmentCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
