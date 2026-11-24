namespace MyWealthV2.Domain.Enums;

/// <summary>In-kind-transfers R14–R17: what a <c>Matched</c> custody movement was matched to.</summary>
public enum CustodyMatchMethod
{
    Transfer = 0,
    Trade = 1,
    CorporateAction = 2
}
