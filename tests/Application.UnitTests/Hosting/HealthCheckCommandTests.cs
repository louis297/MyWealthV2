using System.Net;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Hosting;

// production-hosting R15: the --healthcheck client exits 0 on HTTP 200 and 1 otherwise.
public class HealthCheckCommandTests
{
    private static readonly Uri HealthUri = new("http://localhost:8081/health");

    [Test]
    public async Task Ok_ExitsZero_AndWritesOneLine()
    {
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
            HealthUri,
            TimeSpan.FromSeconds(5),
            output);

        exitCode.ShouldBe(0);
        Lines(output).ShouldHaveSingleItem();
    }

    [Test]
    public async Task ServiceUnavailable_ExitsOne()
    {
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
            HealthUri,
            TimeSpan.FromSeconds(5),
            output);

        exitCode.ShouldBe(1);
        Lines(output).ShouldHaveSingleItem();
    }

    [Test]
    public async Task ConnectionRefused_ExitsOne()
    {
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            new StubHandler(_ => throw new HttpRequestException("Connection refused")),
            HealthUri,
            TimeSpan.FromSeconds(5),
            output);

        exitCode.ShouldBe(1);
        Lines(output).ShouldHaveSingleItem();
    }

    [Test]
    public async Task NoAnswerWithinTimeout_ExitsOne()
    {
        var output = new StringWriter();

        var exitCode = await HealthCheckCommand.RunAsync(
            new StubHandler(async cancellationToken =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            HealthUri,
            TimeSpan.FromMilliseconds(100),
            output);

        exitCode.ShouldBe(1);
        Lines(output).ShouldHaveSingleItem();
    }

    private static string[] Lines(StringWriter output) =>
        output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(cancellationToken);
    }
}
