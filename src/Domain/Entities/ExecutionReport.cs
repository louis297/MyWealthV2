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

    public const int SettlementAccountMaxLength = 34;
    public const int NetAmountDecimals = 4;
    public const decimal MaxNetAmount = 99_999_999_999_999.9999m;

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

    /// <summary>Trade-settlement R6, R7: the broker settlement a <c>Settled</c> or <c>Settlement</c> report names.</summary>
    public string? SettlementId { get; private set; }

    /// <summary>Trade-settlement R7: the trust account number the broker settled against.</summary>
    public string? SettlementAccount { get; private set; }

    /// <summary>Trade-settlement R7: the currency of the broker settlement.</summary>
    public string? Currency { get; private set; }

    /// <summary>Trade-settlement R7: the broker's net, seen from the trust account.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>Trade-settlement R7: the number of fills the broker settled.</summary>
    public int? FillCount { get; private set; }

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
        DateOnly? settledDate,
        Orders.ExecutionReportSettlement? settlement = null)
    {
        if (!IsId(outsideId))
        {
            return false;
        }

        return kind switch
        {
            null => true,
            ExecutionReportKind.Fill => IsId(clientOrderId)
                                        && IsAmount(quantity)
                                        && IsAmount(price)
                                        && tradeDate is { } traded
                                        && settlementDate is { } settles
                                        && settles >= traded,
            ExecutionReportKind.Settled => IsId(clientOrderId) && IsId(fillOutsideId) && settledDate is not null
                                           && (settlement?.SettlementId is null || IsId(settlement.SettlementId)),
            // Trade-settlement R7: a Settlement report names no order.
            ExecutionReportKind.Settlement => settlement is { } closing
                                              && IsId(closing.SettlementId)
                                              && closing.SettlementAccount is { Length: >= 1 and <= SettlementAccountMaxLength }
                                              && IsCurrency(closing.Currency)
                                              && settledDate is not null
                                              && closing.FillCount >= 1
                                              && IsNet(closing.NetAmount),
            ExecutionReportKind.Cancelled or ExecutionReportKind.Expired => IsId(clientOrderId),
            _ => false
        };
    }

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
        DateTimeOffset receivedAt,
        Orders.ExecutionReportSettlement? settlement = null)
    {
        var known = kind is { } value && Enum.IsDefined(value) ? kind : null;
        if (!IsStorable(outsideId, known, clientOrderId, quantity, price, tradeDate, settlementDate, fillOutsideId, settledDate, settlement)
            || string.IsNullOrWhiteSpace(connection) || connection.Length > ConnectionMaxLength)
        {
            throw new DomainException("This execution report cannot be stored.");
        }

        return new ExecutionReport
        {
            PublicId = Guid.NewGuid(),
            Connection = connection,
            OutsideId = outsideId,
            Kind = known,
            ClientOrderId = Cut(clientOrderId, IdMaxLength),
            BrokerOrderId = Cut(brokerOrderId, IdMaxLength),
            // A field its kind does not use is kept only when it fits its column.
            Quantity = Fits(quantity) ? quantity : null,
            Price = Fits(price) ? price : null,
            TradeDate = tradeDate,
            SettlementDate = settlementDate,
            ContractNote = Cut(contractNote, ContractNoteMaxLength),
            FillOutsideId = Cut(fillOutsideId, IdMaxLength),
            SettledDate = settledDate,
            Reason = Cut(reason, ReasonMaxLength),
            SettlementId = Cut(settlement?.SettlementId, IdMaxLength),
            SettlementAccount = Cut(settlement?.SettlementAccount, SettlementAccountMaxLength),
            Currency = IsCurrency(settlement?.Currency) ? settlement!.Currency : null,
            NetAmount = IsNet(settlement?.NetAmount) ? settlement!.NetAmount : null,
            FillCount = settlement?.FillCount,
            ReceivedAt = receivedAt.ToUniversalTime(),
            Status = ExecutionReportStatus.Received
        };
    }

    /// <summary>R25: it changed its order.</summary>
    public void MarkProcessed(DateTimeOffset at)
    {
        EnsureReceived();
        Status = ExecutionReportStatus.Processed;
        ProcessedAt = at.ToUniversalTime();
    }

    /// <summary>R25: it changed nothing, for <paramref name="reason"/>.</summary>
    public void Skip(ExecutionReportSkipReason reason, DateTimeOffset at)
    {
        EnsureReceived();
        Status = ExecutionReportStatus.Skipped;
        SkipReason = reason;
        ProcessedAt = at.ToUniversalTime();
    }

    private void EnsureReceived()
    {
        if (Status != ExecutionReportStatus.Received)
        {
            throw new DomainException($"This execution report is {Status} already.");
        }
    }

    private static bool IsId(string? value) => value is { Length: >= 1 and <= IdMaxLength };

    private static bool IsCurrency(string? value) => value is { Length: 3 } code && code.All(char.IsAsciiLetterUpper);

    /// <summary>Trade-settlement R7: at most 4 decimal places, within <c>decimal(18,4)</c>.</summary>
    private static bool IsNet(decimal? value) =>
        value is { } net && Math.Abs(net) <= MaxNetAmount && decimal.Round(net, NetAmountDecimals) == net;

    /// <summary>Above 0, with at most 8 decimal places, within the column.</summary>
    private static bool IsAmount(decimal? value) => value > 0m && Fits(value);

    private static bool Fits(decimal? value) =>
        value is { } number && Math.Abs(number) <= MaxStoredNumber && decimal.Round(number, Decimals) == number;

    private static string? Cut(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
