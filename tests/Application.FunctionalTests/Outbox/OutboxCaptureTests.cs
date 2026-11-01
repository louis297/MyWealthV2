using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Users.Customers;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Outbox;

public class OutboxCaptureTests : TestBase
{
    [Test]
    public async Task DisableCustomer_CommitsTheUserAndAnOutboxRow_WithoutRevokingTokens()
    {
        var (tenant, adviser, tokens) = await CustomerHttp.SignInTenantAdmin();
        var customerId = await CreateAsync(tokens.AccessToken, adviser.PublicId, "Ned North", "ned@north.example");
        var subject = customerId.ToString();
        var tokenId = Guid.NewGuid().ToString("N");
        await InsertValidTokenAsync(tokenId, subject);

        var rowVersion = await ReadRowVersion(customerId, tokens.AccessToken);
        var disable = await CustomerHttp.Send(
            HttpMethod.Post, $"/users/customers/{customerId}/disable", tokens.AccessToken, new { rowVersion });
        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var item = await ReadItem(customerId, tokens.AccessToken);
        item.RootElement.GetProperty("status").GetString().ShouldBe("disabled");

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var captured = await db.OutboxMessages.AsNoTracking()
            .SingleOrDefaultAsync(row =>
                row.Type == "MyWealthV2.Domain.Events.UserDisabled" && row.Payload.Contains(subject));

        captured.ShouldNotBeNull();
        captured.Status.ShouldBe("Pending");
        captured.TenantId.ShouldBe(tenant.Id);
        captured.Payload.ShouldContain(subject);
        captured.Payload.TrimStart().ShouldStartWith("{");

        var tokenStatus = await db.Database
            .SqlQuery<TokenStatus>(
                $"SELECT [Status] FROM [OpenIddictTokens] WHERE [Id] = {tokenId}")
            .SingleAsync();
        tokenStatus.Status.ShouldBe("valid");
    }

    private static async Task InsertValidTokenAsync(string tokenId, string subject)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [OpenIddictTokens] ([Id], [Subject], [Status], [Type])
            VALUES ({tokenId}, {subject}, N'valid', N'refresh_token')
            """);
    }

    private static async Task<Guid> CreateAsync(string accessToken, Guid adviserId, string name, string email)
    {
        var response = await CustomerHttp.Send(
            HttpMethod.Post, "/users/customers", accessToken, new
            {
                name,
                email,
                password = "Passw0rd!",
                adviserId
            });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<string> ReadRowVersion(Guid id, string accessToken)
    {
        using var json = await ReadItem(id, accessToken);
        return json.RootElement.GetProperty("rowVersion").GetString()!;
    }

    private static async Task<JsonDocument> ReadItem(Guid id, string accessToken)
    {
        var response = await CustomerHttp.Send(HttpMethod.Get, $"/users/customers/{id}", accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed class TokenStatus
    {
        public string? Status { get; set; }
    }
}
