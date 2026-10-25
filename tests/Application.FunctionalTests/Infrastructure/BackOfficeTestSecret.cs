namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Confidential-client secret used only by the functional host. Not an Aspire or production secret.
/// </summary>
public static class BackOfficeTestSecret
{
    public const string Value = "test-back-office-secret";
}
