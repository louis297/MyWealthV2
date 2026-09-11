using MyWealthV2.Application.Common.Behaviours;
using MyWealthV2.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace MyWealthV2.Application.UnitTests.Common.Behaviours;

public class RequestLoggerTests
{
    public sealed record DummyRequest;

    private Mock<ILogger<DummyRequest>> _logger = null!;
    private Mock<IUser> _user = null!;
    private Mock<IIdentityService> _identityService = null!;

    [SetUp]
    public void Setup()
    {
        _logger = new Mock<ILogger<DummyRequest>>();
        _user = new Mock<IUser>();
        _identityService = new Mock<IIdentityService>();
    }

    [Test]
    public async Task ShouldCallGetUserNameAsyncOnceIfAuthenticated()
    {
        _user.Setup(x => x.Id).Returns(Guid.NewGuid().ToString());

        var requestLogger = new LoggingBehaviour<DummyRequest>(_logger.Object, _user.Object, _identityService.Object);

        await requestLogger.Process(new DummyRequest(), new CancellationToken());

        _identityService.Verify(i => i.GetUserNameAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task ShouldNotCallGetUserNameAsyncOnceIfUnauthenticated()
    {
        var requestLogger = new LoggingBehaviour<DummyRequest>(_logger.Object, _user.Object, _identityService.Object);

        await requestLogger.Process(new DummyRequest(), new CancellationToken());

        _identityService.Verify(i => i.GetUserNameAsync(It.IsAny<string>()), Times.Never);
    }
}
