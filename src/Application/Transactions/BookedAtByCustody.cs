using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Transactions;

/// <summary>
/// Custody modes R10–R12: who sets <c>bookedAt</c>. On a Custodial account the server does, from one clock read;
/// on an External account the caller does, as before. Shared by the create handlers and the quote.
/// </summary>
public static class BookedAtByCustody
{
    public const string CustodialMessage = "bookedAt cannot be set on a Custodial account. The server sets it.";

    public const string RequiredMessage = "'Booked At' must not be empty.";

    /// <summary>
    /// Returns the instant to book at. Throws a <c>BookedAt</c> validation error when a Custodial request sends a
    /// value, or when an External request sends none and <paramref name="requiredWhenExternal"/> is set.
    /// </summary>
    public static DateTimeOffset Resolve(
        CustodyMode custodyMode,
        DateTimeOffset? requested,
        TimeProvider time,
        bool requiredWhenExternal = true) =>
        requested ?? default;
}
