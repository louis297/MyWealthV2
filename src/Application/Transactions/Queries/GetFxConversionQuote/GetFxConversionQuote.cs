using System.Text.Json;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Transactions.Queries.GetFxConversionQuote;

/// <summary>FX conversions R20: price a conversion without writing anything.</summary>
[Authorize(Policy = Policies.TransactionsCreate)]
public class GetFxConversionQuoteQuery : IRequest<FxConversionQuoteDto>
{
    public Guid? TenantId { get; init; }

    public Guid? FromAccountId { get; init; }

    public Guid? ToAccountId { get; init; }

    public decimal? SellAmount { get; init; }

    public decimal? BuyAmount { get; init; }

    /// <summary>Optional; defaults to now.</summary>
    public DateTimeOffset? BookedAt { get; init; }

    /// <summary>Not part of the quote body (R20).</summary>
    public decimal? ExpectedRate { get; init; }

    /// <summary>Not part of the quote body (R20).</summary>
    public string? Memo { get; init; }

    /// <summary>Not part of the quote body (R20).</summary>
    public string? Reference { get; init; }

    /// <summary>Not part of the body (R3).</summary>
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
}

public sealed record FxConversionQuoteDto(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    string RateAsOfDate,
    FxQuoteAmountDto Sell,
    FxQuoteAmountDto Buy);

public sealed record FxQuoteAmountDto(decimal Amount, string Currency);

public class GetFxConversionQuoteQueryHandler : IRequestHandler<GetFxConversionQuoteQuery, FxConversionQuoteDto>
{
    public Task<FxConversionQuoteDto> Handle(GetFxConversionQuoteQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
