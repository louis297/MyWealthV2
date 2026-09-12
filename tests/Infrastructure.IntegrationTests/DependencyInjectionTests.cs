using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Infrastructure.Data.Interceptors;
using MyWealthV2.Shared;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests;

public class DependencyInjectionTests
{
    [Test]
    public void ResolvesSaveChangesInterceptorsWithoutMediatR()
    {
        var builder = CreateBuilder();
        builder.Services.AddScoped<IUser, StubUser>();
        builder.AddInfrastructureServices();

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        var interceptors = scope.ServiceProvider.GetServices<ISaveChangesInterceptor>().ToList();

        interceptors.ShouldContain(interceptor => interceptor is AuditableEntityInterceptor);
        interceptors.ShouldNotContain(interceptor => interceptor is DispatchDomainEventsInterceptor);
    }

    [Test]
    public void RegistersDispatchDomainEventsInterceptorWhenMediatorIsPresent()
    {
        var builder = CreateBuilder();
        builder.Services.AddScoped<IUser, StubUser>();
        builder.AddApplicationServices();
        builder.AddInfrastructureServices();

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        var interceptors = scope.ServiceProvider.GetServices<ISaveChangesInterceptor>().ToList();

        interceptors.ShouldContain(interceptor => interceptor is DispatchDomainEventsInterceptor);
    }

    private static HostApplicationBuilder CreateBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"ConnectionStrings:{Services.Database}"] =
            "Server=localhost;Database=unused;TrustServerCertificate=True";
        return builder;
    }

    private sealed class StubUser : IUser
    {
        public string? Id => null;
        public List<string>? Roles => null;
    }
}
