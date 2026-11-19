namespace MyWealthV2.Application.FunctionalTests.Infrastructure;

/// <summary>
/// Confidential-client secret and origin of the <c>simulator</c> client, used only by the functional host.
/// Not an Aspire or production secret.
/// </summary>
public static class SimulatorTestSecret
{
    public const string Value = "test-simulator-secret";

    public const string Origin = "https://localhost/simulator";
}
