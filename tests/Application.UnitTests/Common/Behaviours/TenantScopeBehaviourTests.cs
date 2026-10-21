using MyWealthV2.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Common.Behaviours;

public class TenantScopeBehaviourTests
{
    [Test]
    public async Task RequestWithTenantId_EngagesAccessorBeforeTheHandler()
    {
        var accessor = new RecordingTenantIds();
        using var host = BuildHost(tenantId: 7, accessor);
        var mediator = host.Services.GetRequiredService<ISender>();

        await mediator.Send(new ProbeRequest());

        accessor.EngagedDuringHandler.ShouldBe(true);
        accessor.TenantDuringHandler.ShouldBe(7);
    }

    [Test]
    public async Task RequestWithNullTenant_LeavesAccessorDisengaged()
    {
        var accessor = new RecordingTenantIds();
        using var host = BuildHost(tenantId: null, accessor);
        var mediator = host.Services.GetRequiredService<ISender>();

        await mediator.Send(new ProbeRequest());

        accessor.EngagedDuringHandler.ShouldBe(false);
        accessor.TenantDuringHandler.ShouldBeNull();
    }

    private static IHost BuildHost(int? tenantId, RecordingTenantIds accessor)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(user => user.TenantId).Returns(tenantId);
        currentUser.Setup(user => user.Id).Returns((string?)null);

        var builder = Host.CreateApplicationBuilder();
        builder.AddApplicationServices();
        builder.Services.AddSingleton(currentUser.Object);
        builder.Services.AddSingleton<IUser>(currentUser.Object);
        builder.Services.AddSingleton<ITenantIdAccessor>(accessor);
        builder.Services.AddSingleton(accessor);
        builder.Services.AddSingleton(new Mock<IIdentityService>().Object);
        builder.Services.AddTransient<IRequestHandler<ProbeRequest, int>, ProbeHandler>();
        return builder.Build();
    }

    public sealed record ProbeRequest : IRequest<int>;

    private sealed class ProbeHandler(RecordingTenantIds accessor) : IRequestHandler<ProbeRequest, int>
    {
        public Task<int> Handle(ProbeRequest request, CancellationToken cancellationToken)
        {
            accessor.EngagedDuringHandler = accessor.IsEngaged;
            accessor.TenantDuringHandler = accessor.TenantId;
            return Task.FromResult(1);
        }
    }

    private sealed class RecordingTenantIds : ITenantIdAccessor
    {
        public int? TenantId { get; private set; }

        public bool IsEngaged { get; private set; }

        public bool? EngagedDuringHandler { get; set; }

        public int? TenantDuringHandler { get; set; }

        public void Engage(int tenantId)
        {
            TenantId = tenantId;
            IsEngaged = true;
        }
    }
}
