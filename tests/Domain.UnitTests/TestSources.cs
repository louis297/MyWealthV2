using MyWealthV2.Domain.ValueObjects;

namespace MyWealthV2.Domain.UnitTests;

/// <summary>The source the existing Domain tests pass to the Transaction factories and <c>Reverse</c>.</summary>
internal static class TestSources
{
    public static TransactionSource Manual => TransactionSource.Manual;
}
