using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Users.EventHandlers;
using MyWealthV2.Domain.Events;
using MyWealthV2.Infrastructure.Data;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

public class RevokeTokensTenantScopeTests
{
    [Test]
    public async Task Handle_EngagesTheDisabledTenantBeforeListingSubjects()
    {
        var accessor = new TenantIdAccessor();
        await using var db = OpenDb(accessor);
        var (tenantA, tenantB) = await TwoTenantFixture.InsertAsync(db);
        var (adminA, adminB) = await TwoTenantFixture.InsertTenantAdminsAsync(db, tenantA, tenantB);

        var gate = UsersGate.Create(db, accessor);
        var revocation = new RecordingRevocation();
        var provider = new FixedProvider(gate.Proxy, revocation, accessor);
        var handler = new RevokeTokensOnTenantDisabled(new FixedScopeFactory(provider));

        await handler.Handle(new TenantDisabled(tenantA), CancellationToken.None);

        gate.UsersReadWhileEngaged.ShouldBe(true);
        accessor.TenantId.ShouldBe(tenantA.Id);
        revocation.Subjects.Select(Guid.Parse).ShouldBe([adminA.PublicId]);
        revocation.Subjects.Select(Guid.Parse).ShouldNotContain(adminB.PublicId);
    }

    private static ApplicationDbContext OpenDb(TenantIdAccessor accessor)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SchemaDatabaseSetup.ConnectionString)
            .Options;
        return new ApplicationDbContext(options, accessor);
    }

    public class UsersGate : DispatchProxy
    {
        private ApplicationDbContext _inner = null!;
        private TenantIdAccessor _accessor = null!;

        public IApplicationDbContext Proxy => (IApplicationDbContext)(object)this;

        public bool? UsersReadWhileEngaged { get; private set; }

        public static UsersGate Create(ApplicationDbContext inner, TenantIdAccessor accessor)
        {
            var proxy = DispatchProxy.Create<IApplicationDbContext, UsersGate>();
            var gate = (UsersGate)(object)proxy;
            gate._inner = inner;
            gate._accessor = accessor;
            return gate;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Users")
            {
                UsersReadWhileEngaged = _accessor.IsEngaged;
                return ((IApplicationDbContext)_inner).Users;
            }

            return targetMethod!.Invoke(_inner, args);
        }
    }

    private sealed class RecordingRevocation : ITokenRevocation
    {
        public List<string> Subjects { get; } = [];

        public Task RevokeSubject(string subject, CancellationToken cancellationToken)
        {
            Subjects.Add(subject);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedScopeFactory(IServiceProvider provider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new FixedScope(provider);
    }

    private sealed class FixedScope(IServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider => provider;

        public void Dispose()
        {
        }
    }

    private sealed class FixedProvider(
        IApplicationDbContext db,
        ITokenRevocation revocation,
        ITenantIdAccessor tenantIds) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IApplicationDbContext))
            {
                return db;
            }

            if (serviceType == typeof(ITokenRevocation))
            {
                return revocation;
            }

            if (serviceType == typeof(ITenantIdAccessor))
            {
                return tenantIds;
            }

            return null;
        }
    }
}
