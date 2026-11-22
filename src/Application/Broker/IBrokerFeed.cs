using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Broker;

/// <summary>
/// The broker feed port (docs/features/orders.md R23, R67; ADR 0021 rule 1): one page of the execution reports of one
/// broker connection after a cursor. One feed per connection, for every Tenant that uses it. Infrastructure dispatches
/// to the adapter of the connection. A failure comes back as a result, never as an exception the caller must parse.
/// </summary>
public interface IBrokerFeed
{
    Task<BrokerFeedResult> GetReportsAsync(string connection, string? cursor, int limit, CancellationToken cancellationToken);
}

/// <summary>An execution report as the party sent it. A field the party left out or sent in a shape the adapter
/// cannot read is null, and the pull refuses the page when its kind needs it (R24), except <paramref name="Kind"/>:
/// an unknown kind is null and is stored.</summary>
public sealed record BrokerFeedItem(
    string? Id,
    ExecutionReportKind? Kind,
    string? ClientOrderId,
    string? BrokerOrderId,
    decimal? Quantity,
    decimal? Price,
    DateOnly? TradeDate,
    DateOnly? SettlementDate,
    string? ContractNote,
    string? FillId,
    DateOnly? SettledDate,
    string? Reason);

/// <summary>One page: its items, oldest first, the party's new cursor and whether more items follow.</summary>
public sealed record BrokerFeedPage(IReadOnlyList<BrokerFeedItem> Items, string? Cursor, bool HasMore);

/// <summary>A page, or the reason the call failed.</summary>
public sealed class BrokerFeedResult
{
    private BrokerFeedResult(BrokerFeedPage? page, BrokerFailure? failure)
    {
        Page = page;
        Failure = failure;
    }

    public BrokerFeedPage? Page { get; }

    public BrokerFailure? Failure { get; }

    public static BrokerFeedResult Succeeded(BrokerFeedPage page) => new(page, null);

    public static BrokerFeedResult Failed(BrokerFailure failure) => new(null, failure);
}
