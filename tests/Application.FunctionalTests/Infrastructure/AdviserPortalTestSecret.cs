namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Confidential-client secret used only by the functional host. Not an Aspire or production secret.
/// </summary>
public static class AdviserPortalTestSecret
{
    public const string Value = "test-adviser-portal-secret";
}
