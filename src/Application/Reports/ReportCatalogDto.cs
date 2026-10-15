namespace MyWealthV2.Application.Reports;

public sealed class ReportCatalogDto
{
    public required Guid Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Function { get; init; }

    public required string Scope { get; init; }

    public required IReadOnlyList<string> AllowedTriggers { get; init; }
}
