using System.Globalization;

namespace MyWealthV2.Simulator.Custodian;

/// <summary>In-kind-transfers R64: how the custodian answers new transfers.</summary>
public enum CustodianTransferMode
{
    Execute = 0,
    Hold = 1,
    Reject = 2
}

/// <summary>In-kind-transfers R68: how the custodian rounds a corporate action's new position.</summary>
public enum CustodianRoundingMode
{
    Fractional = 0,
    WholeShares = 1
}

/// <summary>In-kind-transfers R70: what happened to a transfer instruction.</summary>
public enum CustodianTransferStatus
{
    Held = 0,
    Executed = 1,
    Rejected = 2
}

public enum CustodianMovementKind
{
    Transfer = 0,
    Trade = 1,
    CorporateAction = 2
}

/// <summary>A safekeeping account the simulator custodian holds (R61). Its own books: it never reads the platform.</summary>
public sealed class CustodianAccount
{
    public int Id { get; private set; }

    public string AccountNumber { get; private set; } = "";

    public string Currency { get; private set; } = "";

    public string Name { get; private set; } = "";

    public DateTimeOffset OpenedAt { get; private set; }

    public static CustodianAccount Open(string accountNumber, string currency, string name, DateTimeOffset simulatorNow) => new()
    {
        AccountNumber = accountNumber,
        Currency = currency,
        Name = name,
        OpenedAt = simulatorNow
    };
}

/// <summary>One change of a custodian account's position in one symbol (R60, R65–R68).</summary>
public sealed class CustodianMovement
{
    public const int InDirection = 0;
    public const int OutDirection = 1;

    public int Id { get; private set; }

    public int Number { get; private set; }

    public string AccountNumber { get; private set; } = "";

    public int Place { get; private set; }

    public int Direction { get; private set; }

    public CustodianMovementKind Kind { get; private set; }

    public string Symbol { get; private set; } = "";

    public string Currency { get; private set; } = "";

    public decimal Quantity { get; private set; }

    public DateOnly SettlementDate { get; private set; }

    public string? InstructionId { get; private set; }

    public string? TradeReference { get; private set; }

    public string? CorporateActionId { get; private set; }

    public string? CounterpartyName { get; private set; }

    public string? CounterpartyAccount { get; private set; }

    public string? Reference { get; private set; }

    /// <summary>R66: in the custodian's books and in the positions, but never returned by the feed.</summary>
    public bool OmitFromFeed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>R69: <c>{WorldId:N}-M{n}</c>.</summary>
    public static string MovementId(Guid worldId, int number) => $"{worldId:N}-M{number.ToString(CultureInfo.InvariantCulture)}";

    public decimal Signed => Direction == InDirection ? Quantity : -Quantity;

    public static CustodianMovement Create(
        int number,
        int place,
        string accountNumber,
        int direction,
        CustodianMovementKind kind,
        string symbol,
        string currency,
        decimal quantity,
        DateOnly settlementDate,
        DateTimeOffset createdAt,
        string? instructionId = null,
        string? tradeReference = null,
        string? corporateActionId = null,
        string? counterpartyName = null,
        string? counterpartyAccount = null,
        string? reference = null,
        bool omitFromFeed = false) => new()
    {
        Number = number,
        Place = place,
        AccountNumber = accountNumber,
        Direction = direction,
        Kind = kind,
        Symbol = symbol,
        Currency = currency,
        Quantity = quantity,
        SettlementDate = settlementDate,
        CreatedAt = createdAt,
        InstructionId = instructionId,
        TradeReference = tradeReference,
        CorporateActionId = corporateActionId,
        CounterpartyName = counterpartyName,
        CounterpartyAccount = counterpartyAccount,
        Reference = reference,
        OmitFromFeed = omitFromFeed
    };
}

/// <summary>One transfer instruction the platform sent (R62–R65).</summary>
public sealed class CustodianTransfer
{
    public int Id { get; private set; }

    public int Number { get; private set; }

    public string AccountNumber { get; private set; } = "";

    public string InstructionId { get; private set; } = "";

    /// <summary>The body as sent, to execute a held transfer from.</summary>
    public string Body { get; private set; } = "";

    public byte[] RequestHash { get; private set; } = [];

    public CustodianTransferStatus Status { get; private set; }

    public CustodianTransferMode ModeAtReceipt { get; private set; }

    public string? RejectReason { get; private set; }

    public int? MovementNumber { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>R63: <c>{WorldId:N}-T{n}</c>.</summary>
    public static string TransferId(Guid worldId, int number) => $"{worldId:N}-T{number.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryReadNumber(string id, Guid worldId, out int number)
    {
        number = 0;
        var prefix = $"{worldId:N}-T";
        return id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(id[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    public static CustodianTransfer Receive(
        int number, string accountNumber, string instructionId, string body, byte[] hash, CustodianTransferStatus status,
        CustodianTransferMode mode, string? rejectReason, DateTimeOffset receivedAt) => new()
    {
        Number = number,
        AccountNumber = accountNumber,
        InstructionId = instructionId,
        Body = body,
        RequestHash = hash,
        Status = status,
        ModeAtReceipt = mode,
        RejectReason = rejectReason,
        ReceivedAt = receivedAt
    };

    public void Executed(int movementNumber)
    {
        Status = CustodianTransferStatus.Executed;
        MovementNumber = movementNumber;
    }
}

/// <summary>The custodian's one row of settings (R64, R68).</summary>
public sealed class CustodianSettings
{
    public const int SingletonId = 1;

    private CustodianSettings()
    {
    }

    public int Id { get; private set; } = SingletonId;

    public CustodianTransferMode TransferMode { get; private set; }

    public string? RejectReason { get; private set; }

    public CustodianRoundingMode RoundingMode { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    public void SetTransferMode(CustodianTransferMode mode, string? reason)
    {
        TransferMode = mode;
        RejectReason = mode == CustodianTransferMode.Reject ? reason : null;
    }

    public void SetRoundingMode(CustodianRoundingMode mode) => RoundingMode = mode;
}
