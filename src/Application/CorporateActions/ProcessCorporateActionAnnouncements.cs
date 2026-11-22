namespace MyWealthV2.Application.CorporateActions;

/// <summary>What a processing run did: announcements processed, skipped, and failed (left <c>Received</c>).</summary>
public sealed record AnnouncementProcessingOutcome(int Processed, int Skipped, int Failed);

/// <summary>
/// Turns the stored announcements of one market data connection into corporate actions on every Tenant's instrument
/// with the symbol and quote currency (docs/features/corporate-actions.md R11–R13; ADR 0021 rule 8). Each
/// announcement, oldest first, is processed in its own database transaction that also sets its status. Reads past the
/// tenant query filter and names each Tenant itself.
/// </summary>
public sealed class ProcessCorporateActionAnnouncements
{
    public Task<AnnouncementProcessingOutcome> RunAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
