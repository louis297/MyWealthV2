using System.Text.Json;

namespace MyWealthV2.Application.Reports;

/// <summary>The parameters of the FIF report (docs/features/fif-report.md R6, R7).</summary>
public static class CustomerFifBinder
{
    public const string Code = "customer-fif";

    public static CustomerFifBinding Bind(JsonElement parameters, DateOnly today) => new(Guid.Empty, 0, string.Empty);
}

public sealed record CustomerFifBinding(Guid CustomerId, int TaxYear, string SnapshotJson);
