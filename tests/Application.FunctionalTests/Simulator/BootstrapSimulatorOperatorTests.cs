using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.FunctionalTests.Hosting;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Simulator;

// Simulator spec R9: webapi creates the bootstrap SimulatorOperator on start, and fails before it listens when
// the configuration is incomplete.
public class BootstrapSimulatorOperatorTests
{
    private const string Email = "sim-owner@example.test";
    private const string Password = "Bootstrap1!";

    [SetUp]
    public async Task StartWithoutOperator()
    {
        await TestApp.ResetState();
        await FunctionalTestSetup.ReseedAsync();

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identityIds = await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SimulatorOperator)
            .Select(user => user.IdentityUserId)
            .ToListAsync();
        await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SimulatorOperator)
            .ExecuteDeleteAsync();
        await db.Users.Where(user => identityIds.Contains(user.Id)).ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task RestoreSeed()
    {
        await TestApp.ResetState();
        await FunctionalTestSetup.ReseedAsync();
    }

    [Test]
    public async Task WebApiStart_BothKeysSet_CreatesTheOperator()
    {
        using (var factory = ProductionWebApi(new Dictionary<string, string>
               {
                   [SimulatorOperatorBootstrap.EmailKey] = Email,
                   [SimulatorOperatorBootstrap.PasswordKey] = Password
               }))
        {
            _ = factory.Server;
        }

        (await OperatorEmailsAsync()).ShouldBe([Email]);
    }

    [Test]
    public async Task WebApiStart_OnlyEmailSet_FailsBeforeListeningAndCreatesNobody()
    {
        using var factory = ProductionWebApi(new Dictionary<string, string>
        {
            [SimulatorOperatorBootstrap.EmailKey] = Email
        });

        var message = StartupFailure.Capture(factory);

        message.ShouldContain(SimulatorOperatorBootstrap.PasswordKey);
        (await OperatorEmailsAsync()).ShouldBeEmpty();
    }

    private static WebApiFactory ProductionWebApi(IReadOnlyDictionary<string, string> settings) =>
        new(FunctionalTestSetup.ConnectionString, environment: "Production", settings: settings);

    private static async Task<List<string>> OperatorEmailsAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.DomainUsers.IgnoreQueryFilters()
            .Where(user => user.Role == UserRole.SimulatorOperator)
            .Select(user => user.Email)
            .ToListAsync();
    }
}
