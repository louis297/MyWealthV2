using System.Text.Json;

namespace MyWealthV2.Application.Reports;

public sealed class TenantReportGrantDto
{
    public required Guid Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Function { get; init; }

    public required string Scope { get; init; }

    public required IReadOnlyList<string> AllowedTriggers { get; init; }

    public required bool IsActive { get; init; }

    public static IReadOnlyList<string> ParseTriggers(string json) =>
        JsonSerializer.Deserialize<string[]>(json) ?? [];
}
