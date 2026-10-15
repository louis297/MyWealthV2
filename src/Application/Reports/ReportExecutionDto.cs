using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyWealthV2.Application.Reports;

public sealed class ReportExecutionDto
{
    public required Guid Id { get; init; }

    public required Guid ReportId { get; init; }

    public required string Status { get; init; }

    public required string TriggerKind { get; init; }

    public required DateTimeOffset Created { get; init; }

    public required JsonElement Parameters { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? AsOf { get; init; }
}
