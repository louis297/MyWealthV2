using System.Text.Json;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Transactions.Commands.CreateFxConversion;

/// <summary>FX conversions §7: convert cash between two Accounts of one Customer in different currencies.</summary>
[Authorize(Policy = Policies.TransactionsCreate)]
public class CreateFxConversionCommand : IRequest<IdempotentResult>
{
    public Guid? TenantId { get; init; }

    public Guid? FromAccountId { get; init; }

    public Guid? ToAccountId { get; init; }

    public decimal? SellAmount { get; init; }

    public decimal? BuyAmount { get; init; }

    public decimal? ExpectedRate { get; init; }

    public DateTimeOffset? BookedAt { get; init; }

    public string? Memo { get; init; }

    public string? Reference { get; init; }

    /// <summary>Not part of the body (R3). Declared only so that a request that sends it is rejected.</summary>
    public string? Type { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public Guid? AccountId { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public string? Currency { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public decimal? Amount { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public decimal? Rate { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public JsonElement? Cash { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public JsonElement? Security { get; init; }

    /// <summary>Not part of the body (R3).</summary>
    public JsonElement? Legs { get; init; }

    public string? IdempotencyKey { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestPath { get; set; }
}

public class CreateFxConversionCommandHandler : IRequestHandler<CreateFxConversionCommand, IdempotentResult>
{
    public Task<IdempotentResult> Handle(CreateFxConversionCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
