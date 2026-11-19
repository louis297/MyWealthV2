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

// Simulator spec R9 and R10: the bootstrap SimulatorOperator follows identity-auth A25 and A27–A31 with the
// Bootstrap:SimulatorOperator:* keys, independent of the bootstrap SystemAdmin.
[NonParallelizable]
public class SimulatorOperatorBootstrapTests
{
    private const string Email = "sim-owner@example.test";
    private const string Password = "Bootstrap1!";

    private CapturingLoggerProvider _logs = null!;

    [SetUp]
    public async Task DeletePlatformPeople()
    {
        _logs = new CapturingLoggerProvider();
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var platformRoles = new[] { UserRole.SystemAdmin, UserRole.SimulatorOperator };
        var identityIds = await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => platformRoles.Contains(user.Role))
            .Select(user => user.IdentityUserId)
            .ToListAsync();
        await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => platformRoles.Contains(user.Role))
            .ExecuteDeleteAsync();
        await db.Users.Where(user => identityIds.Contains(user.Id)).ExecuteDeleteAsync();
    }

    [TearDown]
    public void DisposeLogs() => _logs.Dispose();

    [Test]
    public async Task NoOperator_BothKeysValid_CreatesOneActiveOperatorWithMatchingIdentityUser()
    {
        await using var provider = BuildProvider();

        await RunAsync(provider, Settings(Email, Password, name: "Owner"));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var operators = await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SimulatorOperator)
            .ToListAsync();
        var person = operators.ShouldHaveSingleItem();
        person.Email.ShouldBe(Email);
        person.Name.ShouldBe("Owner");
        person.Status.ShouldBe(UserStatus.Active);
        person.TenantId.ShouldBeNull();
        person.AdviserId.ShouldBeNull();

        var identityUser = await db.Users.SingleAsync(user => user.Id == person.IdentityUserId);
        identityUser.UserName.ShouldBe(person.PublicId.ToString());
        identityUser.Email.ShouldBe(Email);
        identityUser.EmailConfirmed.ShouldBeTrue();
        identityUser.TenantId.ShouldBeNull();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await userManager.CheckPasswordAsync(identityUser, Password)).ShouldBeTrue();
    }

    [Test]
    public async Task NoOperator_NameNotSet_UsesTheDefaultName()
    {
        await using var provider = BuildProvider();

        await RunAsync(provider, Settings(Email, Password));

        (await OperatorsAsync(provider)).ShouldHaveSingleItem().Name.ShouldBe(SimulatorOperatorBootstrap.DefaultName);
    }

    [Test]
    public async Task NoOperator_NeitherKeySet_CreatesNobodyAndLogsAWarning()
    {
        await using var provider = BuildProvider();

        await RunAsync(provider, Settings(email: null, password: null));

        (await OperatorsAsync(provider)).ShouldBeEmpty();
        _logs.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains(SimulatorOperatorBootstrap.EmailKey));
    }

    [TestCase(UserStatus.Active)]
    [TestCase(UserStatus.Disabled)]
    public async Task OperatorExists_OtherKeysSet_ChangesNothing(UserStatus status)
    {
        await using var provider = BuildProvider();
        await RunAsync(provider, Settings(Email, Password));
        if (status == UserStatus.Disabled)
        {
            await DisableOperatorAsync(provider);
        }

        var before = (await OperatorsAsync(provider)).ShouldHaveSingleItem();
        var hashBefore = await PasswordHashAsync(provider, before.IdentityUserId);

        await RunAsync(provider, Settings("other@example.test", "Different1!", name: "Other"));

        var after = (await OperatorsAsync(provider)).ShouldHaveSingleItem();
        after.ShouldBe(before);
        after.Status.ShouldBe(status);
        (await PasswordHashAsync(provider, after.IdentityUserId)).ShouldBe(hashBefore);
    }

    [Test]
    public async Task OperatorExists_PasswordInvalid_DoesNotCheckThePassword()
    {
        await using var provider = BuildProvider();
        await RunAsync(provider, Settings(Email, Password));

        await Should.NotThrowAsync(() => RunAsync(provider, Settings("other@example.test", "short")));

        (await OperatorsAsync(provider)).ShouldHaveSingleItem().Email.ShouldBe(Email);
    }

    [Test]
    public async Task NoOperator_OnlyEmailSet_ThrowsNamingThePasswordKey()
    {
        await using var provider = BuildProvider();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => RunAsync(provider, Settings(Email, password: null)));

        exception.Message.ShouldContain(SimulatorOperatorBootstrap.PasswordKey);
        await ShouldHaveNoPersonAsync(provider, Email);
    }

    [Test]
    public async Task NoOperator_OnlyPasswordSet_ThrowsNamingTheEmailKey()
    {
        await using var provider = BuildProvider();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => RunAsync(provider, Settings(email: null, Password)));

        exception.Message.ShouldContain(SimulatorOperatorBootstrap.EmailKey);
        exception.Message.ShouldNotContain(Password);
        (await OperatorsAsync(provider)).ShouldBeEmpty();
    }

    [Test]
    public async Task NoOperator_PasswordFailsTheRules_ThrowsWithRuleCodesAndWithoutThePassword()
    {
        await using var provider = BuildProvider();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => RunAsync(provider, Settings(Email, "short")));

        exception.Message.ShouldContain(SimulatorOperatorBootstrap.PasswordKey);
        exception.Message.ShouldContain("PasswordTooShort");
        exception.Message.ShouldNotContain("short", Case.Sensitive);
        await ShouldHaveNoPersonAsync(provider, Email);
    }

    [Test]
    public async Task NoOperator_EmailInvalid_ThrowsNamingTheEmailKey()
    {
        await using var provider = BuildProvider();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => RunAsync(provider, Settings("not-an-email", Password)));

        exception.Message.ShouldContain(SimulatorOperatorBootstrap.EmailKey);
        exception.Message.ShouldNotContain(Password);
        await ShouldHaveNoPersonAsync(provider, "not-an-email");
    }

    [Test]
    public async Task ExistingSystemAdmin_DoesNotStopTheOperator()
    {
        await using var provider = BuildProvider();
        await RunSystemAdminAsync(provider, "admin@example.test", Password);

        await RunAsync(provider, Settings(Email, Password));

        (await OperatorsAsync(provider)).ShouldHaveSingleItem().Email.ShouldBe(Email);
    }

    [Test]
    public async Task ExistingOperator_DoesNotStopTheSystemAdmin()
    {
        await using var provider = BuildProvider();
        await RunAsync(provider, Settings(Email, Password));

        await RunSystemAdminAsync(provider, "admin@example.test", Password);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.DomainUsers.IgnoreQueryFilters().CountAsync(user => user.Role == UserRole.SystemAdmin)).ShouldBe(1);
    }

    [Test]
    public async Task SameEmailAsASystemAdmin_FailsAndCreatesNothing()
    {
        await using var provider = BuildProvider();
        await RunSystemAdminAsync(provider, Email, Password);

        await Should.ThrowAsync<Exception>(() => RunAsync(provider, Settings(Email, Password)));

        (await OperatorsAsync(provider)).ShouldBeEmpty();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.CountAsync(user => user.Email == Email)).ShouldBe(1);
    }

    [Test]
    public async Task DevelopmentSeed_CreatesTheOperatorOnce()
    {
        await using var provider = BuildProvider();

        await SeedAsync(provider);
        await SeedAsync(provider);

        var person = (await OperatorsAsync(provider)).ShouldHaveSingleItem();
        person.Email.ShouldBe(DevelopmentIdentitySeeder.SimulatorOperatorEmail);
        person.Status.ShouldBe(UserStatus.Active);
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var identityUser = await userManager.FindByIdAsync(person.IdentityUserId);
        (await userManager.CheckPasswordAsync(identityUser!, DevelopmentIdentitySeeder.SystemAdminPassword)).ShouldBeTrue();
    }

    private static async Task SeedAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await DevelopmentIdentitySeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private static async Task RunSystemAdminAsync(ServiceProvider provider, string email, string password)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [SystemAdminBootstrap.EmailKey] = email,
            [SystemAdminBootstrap.PasswordKey] = password
        }).Build();
        await using var scope = provider.CreateAsyncScope();
        await SystemAdminBootstrap.RunAsync(scope.ServiceProvider, configuration, CancellationToken.None);
    }

    private static async Task ShouldHaveNoPersonAsync(ServiceProvider provider, string email)
    {
        (await OperatorsAsync(provider)).ShouldBeEmpty();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.AnyAsync(user => user.Email == email)).ShouldBeFalse();
    }

    private static async Task DisableOperatorAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.DomainUsers.IgnoreQueryFilters().SingleAsync(user => user.Role == UserRole.SimulatorOperator);
        person.Disable();
        await db.SaveChangesAsync();
    }

    private static async Task<string?> PasswordHashAsync(ServiceProvider provider, string identityUserId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Users.Where(user => user.Id == identityUserId).Select(user => user.PasswordHash).SingleAsync();
    }

    private static Dictionary<string, string?> Settings(string? email, string? password, string? name = null)
    {
        var settings = new Dictionary<string, string?>();
        if (email is not null) settings[SimulatorOperatorBootstrap.EmailKey] = email;
        if (password is not null) settings[SimulatorOperatorBootstrap.PasswordKey] = password;
        if (name is not null) settings[SimulatorOperatorBootstrap.NameKey] = name;
        return settings;
    }

    private static async Task RunAsync(ServiceProvider provider, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        await using var scope = provider.CreateAsyncScope();
        await SimulatorOperatorBootstrap.RunAsync(scope.ServiceProvider, configuration, CancellationToken.None);
    }

    private static async Task<List<OperatorRow>> OperatorsAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SimulatorOperator)
            .Select(user => new OperatorRow(user.Email, user.Name, user.Status, user.IdentityUserId))
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

    private sealed record OperatorRow(string Email, string Name, UserStatus Status, string IdentityUserId);

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
