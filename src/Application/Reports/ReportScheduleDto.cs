using System.Text.Json;

namespace MyWealthV2.Application.Reports;

public sealed class ReportScheduleDto
{
    public required Guid Id { get; init; }

    public required Guid ReportId { get; init; }

    public required JsonElement Parameters { get; init; }

    public required string Cron { get; init; }

    public required bool IsActive { get; init; }

    public required string RowVersion { get; init; }
}
