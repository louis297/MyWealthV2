namespace MyWealthV2.Application.Common.Exceptions;

/// <summary>
/// A save failed on the unique index <c>UX_Accounts_DepositReference</c>: another open drew the same reference
/// at the same time (client-money-accounts R17 b). Raised by the database context; Application never sees the
/// <c>SqlException</c>.
/// </summary>
public sealed class DepositReferenceTakenException(Exception innerException)
    : Exception("The deposit reference is already used by another account.", innerException);
