using System.Net;

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
}
