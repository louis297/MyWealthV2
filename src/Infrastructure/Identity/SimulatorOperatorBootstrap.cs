using Microsoft.Extensions.Configuration;

namespace MyWealthV2.Infrastructure.Identity;

/// <summary>
/// Creates the first SimulatorOperator from configuration when the database has none
/// (docs/features/simulator.md R9; identity-auth amendment 2026-11-19).
/// </summary>
public static class SimulatorOperatorBootstrap
{
    public const string EmailKey = "Bootstrap:SimulatorOperator:Email";
    public const string PasswordKey = "Bootstrap:SimulatorOperator:Password";
    public const string NameKey = "Bootstrap:SimulatorOperator:Name";
    public const string DefaultName = "Simulator Operator";

    public static Task RunAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
