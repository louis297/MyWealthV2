using System.Net;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Transactions;

public class CreateTransactionTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401WithoutRedirect()
    {
        var post = await TransactionHttp.Send(HttpMethod.Post, "/transactions", accessToken: null, new
        {
            accountId = Guid.NewGuid(),
            type = "TransferIn",
            amount = 100.00m,
            bookedAt = "2026-09-24T09:00:00+12:00"
        });
        var get = await TransactionHttp.Send(HttpMethod.Get, "/transactions", accessToken: null);

        post.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        post.Headers.Location.ShouldBeNull();
        get.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        get.Headers.Location.ShouldBeNull();
    }

    [Test]
    public async Task Customer_Returns403()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm C", "firmc");
        var adviser = await TestApp.CreatePersonAsync(
            UserRole.Adviser, "Cara", "cara@firmc", "Password1!", tenant.Id);
        var customer = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Dee", "dee@firmc", "Password1!", tenant.Id, adviser.Id);
        var tokens = await TestApp.IssueAccessTokenAsync(customer);
        var body = new
        {
            accountId = Guid.NewGuid(),
            type = "TransferIn",
            amount = 100.00m,
            bookedAt = "2026-09-24T09:00:00+12:00"
        };

        var post = await TransactionHttp.Send(
            HttpMethod.Post, "/transactions", tokens.AccessToken, body, Guid.NewGuid());
        var list = await TransactionHttp.Send(HttpMethod.Get, "/transactions", tokens.AccessToken);
        var item = await TransactionHttp.Send(
            HttpMethod.Get, $"/transactions/{Guid.NewGuid()}", tokens.AccessToken);

        post.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        item.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
