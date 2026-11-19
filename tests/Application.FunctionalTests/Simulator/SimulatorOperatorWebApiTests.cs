using System.Net;
using System.Net.Http.Headers;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Simulator;

/// <summary>Simulator spec R3 (identity-auth A51): a SimulatorOperator token is unauthenticated on every webapi route.</summary>
[Parallelizable]
public class SimulatorOperatorWebApiTests : TestBase
{
    [TestCase("/users/me")]
    [TestCase("/currencies")]
    [TestCase("/accounts")]
    [TestCase("/transactions")]
    [TestCase("/tenants")]
    public async Task OperatorToken_Returns401(string path)
    {
        var person = await TestApp.CreatePersonAsync(
            UserRole.SimulatorOperator, "Sim", "sim-op@localhost", "Password1!");
        var tokens = await TestApp.IssueAccessTokenAsync(person);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var response = await FunctionalTestSetup.WebClient.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Location.ShouldBeNull();
    }
}
