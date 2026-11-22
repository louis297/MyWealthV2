namespace MyWealthV2.Application.Broker;

/// <summary>What a processing run did: reports processed, skipped, and failed or left <c>Received</c> for the next
/// run.</summary>
public sealed record ReportProcessingOutcome(int Processed, int Skipped, int Failed);

/// <summary>
/// Turns the stored execution reports of one broker connection into changes of their orders
/// (docs/features/orders.md R25–R29; ADR 0021 rule 8).
/// </summary>
public sealed class ProcessExecutionReports
{
    public Task<ReportProcessingOutcome> RunAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
