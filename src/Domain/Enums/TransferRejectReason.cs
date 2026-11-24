namespace MyWealthV2.Domain.Enums;

/// <summary>In-kind-transfers R35: why the custodian refused a transfer instruction. A reason the adapter does not know
/// is <see cref="Refused"/>.</summary>
public enum TransferRejectReason
{
    UnknownInstrument = 0,
    InsufficientPosition = 1,
    CounterpartyRefused = 2,
    Refused = 3
}
