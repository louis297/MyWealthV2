using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Infrastructure.IntegrationTests.AppliedSchema;

// identity-auth amendment 2026-11-12, A25 and A27–A31. A26 is a functional test.
[NonParallelizable]
public class SystemAdminBootstrapTests
{
    private const string Email = "owner@example.test";
    private const string Password = "Bootstrap1!";

    private CapturingLoggerProvider _logs = null!;

    [SetUp]
    public async Task DeleteSystemAdmins()
    {
        _logs = new CapturingLoggerProvider();
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identityIds = await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SystemAdmin)
            .Select(user => user.IdentityUserId)
            .ToListAsync();
        await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SystemAdmin)
            .ExecuteDeleteAsync();
        await db.Users.Where(user => identityIds.Contains(user.Id)).ExecuteDeleteAsync();
    }

    [TearDown]
    public void DisposeLogs() => _logs.Dispose();

    [Test]
    public async Task NoSystemAdmin_BothKeysValid_CreatesOneActiveSystemAdminWithMatchingIdentityUser()
    {
        await using var provider = BuildProvider();

        await RunAsync(provider, Settings(Email, Password, name: "Owner"));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admins = await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SystemAdmin)
            .ToListAsync();
        var admin = admins.ShouldHaveSingleItem();
        admin.Email.ShouldBe(Email);
        admin.Name.ShouldBe("Owner");
        admin.Status.ShouldBe(UserStatus.Active);
        admin.TenantId.ShouldBeNull();
        admin.AdviserId.ShouldBeNull();

        var identityUser = await db.Users.SingleAsync(user => user.Id == admin.IdentityUserId);
        identityUser.UserName.ShouldBe(admin.PublicId.ToString());
        identityUser.Email.ShouldBe(Email);
        identityUser.EmailConfirmed.ShouldBeTrue();
        identityUser.TenantId.ShouldBeNull();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await userManager.CheckPasswordAsync(identityUser, Password)).ShouldBeTrue();
    }

    [Test]
    public async Task NoSystemAdmin_NameNotSet_UsesTheDefaultName()
    {
        await using var provider = BuildProvider();

        await RunAsync(provider, Settings(Email, Password));

        var admin = (await SystemAdminsAsync(provider)).ShouldHaveSingleItem();
        admin.Name.ShouldBe(SystemAdminBootstrap.DefaultName);
    }

    [Test]
    public async Task NoSystemAdmin_NeitherKeySet_CreatesNobodyAndLogsAWarning()
    {
        await using var provider = BuildProvider();

        await RunAsync(provider, Settings(email: null, password: null));

        (await SystemAdminsAsync(provider)).ShouldBeEmpty();
        _logs.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
    }

    private static Dictionary<string, string?> Settings(string? email, string? password, string? name = null)
    {
        var settings = new Dictionary<string, string?>();
        if (email is not null) settings[SystemAdminBootstrap.EmailKey] = email;
        if (password is not null) settings[SystemAdminBootstrap.PasswordKey] = password;
        if (name is not null) settings[SystemAdminBootstrap.NameKey] = name;
        return settings;
    }

    private static async Task RunAsync(ServiceProvider provider, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        await using var scope = provider.CreateAsyncScope();
        await SystemAdminBootstrap.RunAsync(scope.ServiceProvider, configuration, CancellationToken.None);
    }

    private static async Task<List<SystemAdminRow>> SystemAdminsAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SystemAdmin)
            .Select(user => new SystemAdminRow(user.Email, user.Name, user.Status, user.IdentityUserId))
            .ToListAsync();
    }

    // Mirrors the Identity options in src/Infrastructure/DependencyInjection.cs.
    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_logs));
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(SchemaDatabaseSetup.ConnectionString));
        services.AddScoped<ITenantIdAccessor, TenantIdAccessor>();
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version2;
                options.Password.RequiredLength = 8;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed record SystemAdminRow(string Email, string Name, UserStatus Status, string IdentityUserId);

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}
