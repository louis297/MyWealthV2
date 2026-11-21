using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyWealthV2.Application.BankFeed;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.BankFeed;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.BankFeed;

/// <summary>Withdrawals R49–R51, R58: <c>SimulatorBankPayments</c> against canned HTTP.</summary>
public class SimulatorBankPaymentsTests
{
    private const string Key = "test-party-api-key-0123456789abcdef";

    private static readonly BankConnectionSettings Connection = new(
        "simulator", "Simulator", new Uri("http://simulator:8082"), Key, TimeoutSeconds: 1, PollMinutes: 1);

    private static readonly Guid Instruction = Guid.Parse("6b1f0c2e-7a4d-4b8e-9c3a-2f5e8d1b0a77");

    private static readonly BankPaymentRequest Request = new(
        "simulator", "SIM-DEMO USD", Instruction, 300.00m, "USD", "K7M3Q9PXB", new BankPaymentPayee("06-0123-0456789-00", "J Smith"));

    [Test]
    public async Task Accepted_ReturnsThePaymentId_AndSendsTheBodyTheKeyAndTheAccount()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "paymentId": "3f2c-P4", "status": "Accepted" }""");

        var result = await Payments(handler).SendAsync(Connection, Request, CancellationToken.None);

        result.PaymentId.ShouldBe("3f2c-P4");
        result.RejectReason.ShouldBeNull();
        result.Failure.ShouldBeNull();
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.RequestUri!.AbsoluteUri.ShouldBe("http://simulator:8082/party/bank/accounts/SIM-DEMO%20USD/payments");
        sent.Headers.GetValues("X-Api-Key").ShouldBe([Key]);
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        var root = body.RootElement;
        root.GetProperty("instructionId").GetString().ShouldBe(Instruction.ToString());
        root.GetProperty("amount").GetDecimal().ShouldBe(300.00m);
        root.GetProperty("amount").ValueKind.ShouldBe(JsonValueKind.Number);
        root.GetProperty("currency").GetString().ShouldBe("USD");
        root.GetProperty("reference").GetString().ShouldBe("K7M3Q9PXB");
        root.GetProperty("payee").GetProperty("accountNumber").GetString().ShouldBe("06-0123-0456789-00");
        root.GetProperty("payee").GetProperty("name").GetString().ShouldBe("J Smith");
    }

    [Test]
    public async Task AReceiptReturn_SendsTheReturnedLine_AndNoPayee()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "paymentId": "3f2c-P6", "status": "Accepted" }""");
        var request = new BankPaymentRequest(
            "simulator", "SIM-DEMO USD", Instruction, 250.50m, "USD", null, null, ReturnOfOutsideId: "3f2c-L7");

        var result = await Payments(handler).SendAsync(Connection, request, CancellationToken.None);

        result.PaymentId.ShouldBe("3f2c-P6");
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        var root = body.RootElement;
        root.GetProperty("instructionId").GetString().ShouldBe(Instruction.ToString());
        root.GetProperty("amount").GetDecimal().ShouldBe(250.50m);
        root.TryGetProperty("returnOf", out var returnOf).ShouldBeTrue();
        returnOf.GetString().ShouldBe("3f2c-L7");
        root.TryGetProperty("payee", out _).ShouldBeFalse();
        root.TryGetProperty("reference", out _).ShouldBeFalse();
    }

    [Test]
    public async Task APayment_SendsNoReturnedLine()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "paymentId": "3f2c-P4", "status": "Accepted" }""");

        await Payments(handler).SendAsync(Connection, Request, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Bodies.Single());
        body.RootElement.TryGetProperty("returnOf", out _).ShouldBeFalse();
    }

    [TestCase("UnknownLine", PaymentRejectReason.UnknownLine)]
    [TestCase("InvalidAccount", PaymentRejectReason.InvalidAccount)]
    [TestCase("AccountClosed", PaymentRejectReason.AccountClosed)]
    [TestCase("InsufficientFunds", PaymentRejectReason.InsufficientFunds)]
    [TestCase("Refused", PaymentRejectReason.Refused)]
    [TestCase("SomethingNew", PaymentRejectReason.Refused)]
    public async Task Rejected_MapsTheReason_AndAnUnknownOneIsRefused(string reason, PaymentRejectReason expected)
    {
        var handler = new StubHandler(HttpStatusCode.OK, $$"""{ "paymentId": "3f2c-P5", "status": "Rejected", "reason": "{{reason}}" }""");

        var result = await Payments(handler).SendAsync(Connection, Request, CancellationToken.None);

        result.RejectReason.ShouldBe(expected);
        result.PaymentId.ShouldBeNull();
        result.Failure.ShouldBeNull();
    }

    [TestCase(HttpStatusCode.Unauthorized, BankFeedFailure.Unauthorized)]
    [TestCase(HttpStatusCode.ServiceUnavailable, BankFeedFailure.Unavailable)]
    [TestCase(HttpStatusCode.NotFound, BankFeedFailure.UnknownAccount)]
    [TestCase(HttpStatusCode.BadRequest, BankFeedFailure.BadResponse)]
    [TestCase(HttpStatusCode.Conflict, BankFeedFailure.BadResponse)]
    [TestCase(HttpStatusCode.InternalServerError, BankFeedFailure.BadResponse)]
    public async Task Status_MapsToItsFailure(HttpStatusCode status, BankFeedFailure expected)
    {
        var result = await Payments(new StubHandler(status, "")).SendAsync(Connection, Request, CancellationToken.None);

        result.Failure.ShouldBe(expected);
        result.PaymentId.ShouldBeNull();
    }

    [TestCase("not json")]
    [TestCase("")]
    [TestCase("null")]
    [TestCase("""{ "status": "Accepted" }""")]
    [TestCase("""{ "paymentId": "P1", "status": "Maybe" }""")]
    public async Task BodyItCannotRead_IsABadResponse(string body)
    {
        var result = await Payments(new StubHandler(HttpStatusCode.OK, body)).SendAsync(Connection, Request, CancellationToken.None);

        result.Failure.ShouldBe(BankFeedFailure.BadResponse);
    }

    [Test]
    public async Task NoAnswerWithinTheTimeout_IsATimeout()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}", delay: TimeSpan.FromSeconds(10));

        (await Payments(handler).SendAsync(Connection, Request, CancellationToken.None)).Failure.ShouldBe(BankFeedFailure.Timeout);
    }

    [Test]
    public async Task RefusedConnection_IsUnreachable()
    {
        var refused = Connection with { BaseUrl = new Uri("http://127.0.0.1:1") };

        (await new SimulatorBankPayments(new Clients(new SocketsHttpHandler())).SendAsync(refused, Request, CancellationToken.None))
            .Failure.ShouldBe(BankFeedFailure.Unreachable);
    }

    [Test]
    public async Task Dispatcher_SendsToTheAdapterOfTheConnection_AndAnUnconfiguredOneIsUnreachable()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{ "paymentId": "3f2c-P4", "status": "Accepted" }""");
        var dispatcher = new BankPaymentsDispatcher(
            new ConfiguredBankConnections([Connection]), [Payments(handler)], NullLogger<BankPaymentsDispatcher>.Instance);

        (await dispatcher.SendAsync(Request, CancellationToken.None)).PaymentId.ShouldBe("3f2c-P4");
        (await dispatcher.SendAsync(Request with { Connection = "other-bank" }, CancellationToken.None))
            .Failure.ShouldBe(BankFeedFailure.Unreachable);
    }

    private static SimulatorBankPayments Payments(HttpMessageHandler handler) => new(new Clients(handler));

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(HttpStatusCode status, string body, TimeSpan? delay = null) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
