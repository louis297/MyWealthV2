namespace MyWealthV2.Domain.Entities;

/// <summary>
/// One thing a broker said about one order, as read through the execution report feed
/// (docs/features/orders.md R23–R25; ADR 0021 rule 7). Platform data with no Tenant, stored once per
/// (connection, outside id), then processed into its order or skipped with a reason.
/// </summary>
public class ExecutionReport
{
    public const int ConnectionMaxLength = 50;
    public const int IdMaxLength = 100;
    public const int ContractNoteMaxLength = 50;
    public const int ReasonMaxLength = 140;
    public const int Decimals = 8;

    /// <summary>The largest value a <c>decimal(18,8)</c> column holds.</summary>
    public const decimal MaxStoredNumber = 9_999_999_999.99999999m;

    private ExecutionReport()
    {
    }

    public long Id { get; private set; }

    public Guid PublicId { get; private set; }

    public string Connection { get; private set; } = string.Empty;

    public string OutsideId { get; private set; } = string.Empty;

    /// <summary>The kind; <c>null</c> for a kind the adapter did not know (R24).</summary>
    public ExecutionReportKind? Kind { get; private set; }

    /// <summary>The party's text; the PublicId of an order when the platform placed it.</summary>
    public string? ClientOrderId { get; private set; }

    public string? BrokerOrderId { get; private set; }

    public decimal? Quantity { get; private set; }

    public decimal? Price { get; private set; }

    public DateOnly? TradeDate { get; private set; }

    public DateOnly? SettlementDate { get; private set; }

    public string? ContractNote { get; private set; }

    /// <summary>On a <c>Settled</c> report: the outside id of the fill's <c>Fill</c> report.</summary>
    public string? FillOutsideId { get; private set; }

    public DateOnly? SettledDate { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public ExecutionReportStatus Status { get; private set; }

    public ExecutionReportSkipReason? SkipReason { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>
    /// R24: what an item needs before its page can be stored. Every item needs an outside id. An item of a known kind
    /// also needs a client order id and its own fields: a <c>Fill</c> a quantity and a price above 0 with at most 8
    /// decimal places, a trade date and a settlement date not before it; a <c>Settled</c> the fill's outside id and a
    /// settled date. An item with no kind needs nothing more.
    /// </summary>
    public static bool IsStorable(
        string? outsideId,
        ExecutionReportKind? kind,
        string? clientOrderId,
        decimal? quantity,
        decimal? price,
        DateOnly? tradeDate,
        DateOnly? settlementDate,
        string? fillOutsideId,
        DateOnly? settledDate) => throw new NotImplementedException();

    /// <summary>R24: a new report, <c>Received</c> at platform time. The contract note is cut to 50 characters and the
    /// reason to 140.</summary>
    public static ExecutionReport Receive(
        string connection,
        string outsideId,
        ExecutionReportKind? kind,
        string? clientOrderId,
        string? brokerOrderId,
        decimal? quantity,
        decimal? price,
        DateOnly? tradeDate,
        DateOnly? settlementDate,
        string? contractNote,
        string? fillOutsideId,
        DateOnly? settledDate,
        string? reason,
        DateTimeOffset receivedAt) => throw new NotImplementedException();

    /// <summary>R25: it changed its order.</summary>
    public void MarkProcessed(DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R25: it changed nothing, for <paramref name="reason"/>.</summary>
    public void Skip(ExecutionReportSkipReason reason, DateTimeOffset at) => throw new NotImplementedException();
}
