using Microsoft.Extensions.Configuration;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Infrastructure.Identity;

/// <summary>
/// Creates the first SimulatorOperator from configuration when the database has none
/// (docs/features/simulator.md R9; identity-auth amendment 2026-11-19). Independent of the bootstrap SystemAdmin.
/// </summary>
public static class SimulatorOperatorBootstrap
{
    public const string EmailKey = "Bootstrap:SimulatorOperator:Email";
    public const string PasswordKey = "Bootstrap:SimulatorOperator:Password";
    public const string NameKey = "Bootstrap:SimulatorOperator:Name";
    public const string DefaultName = "Simulator Operator";

    private static readonly PlatformPersonBootstrap Bootstrap =
        new(UserRole.SimulatorOperator, "SimulatorOperator", EmailKey, PasswordKey, NameKey, DefaultName);

    public static Task RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken) =>
        Bootstrap.RunAsync(services, configuration, cancellationToken);
}
