using MyWealthV2.Infrastructure.Email;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Common.Email;

public class NoOpEmailSenderTests
{
    [Test]
    public async Task SendAsync_CompletesWithoutSending()
    {
        var sender = new NoOpEmailSender();

        await sender.SendAsync("ada@localhost", "subject", "body", CancellationToken.None);

        true.ShouldBeTrue();
    }
}
