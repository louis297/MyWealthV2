namespace MyWealthV2.Domain.Enums;

/// <summary>In-kind-transfers R20: <c>In</c>, shares come from the counterparty into the Account; <c>Out</c>, they
/// leave it for the counterparty.</summary>
public enum TransferDirection
{
    In = 0,
    Out = 1
}
