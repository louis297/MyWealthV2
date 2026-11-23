using MyWealthV2.Domain.Migrations;

namespace MyWealthV2.Domain.Entities;

/// <summary>
/// A Tenant's record of one load of opening positions from a previous provider into empty Custodial accounts
/// (docs/features/migration-batches.md §4.2–§4.6): a header, control totals, entries and a status. It writes no
/// Transaction by itself; <c>PostMigrationBatch</c> writes them and then calls <see cref="MarkPosted"/>.
/// </summary>
public class MigrationBatch : BaseAuditableEntity
{
    public const int TextMaxLength = 100;
    public const int NoteMaxLength = 200;
    public const int MaxEntries = 500;
    public const int MaxHoldings = 5000;
    public const int CashDecimals = 4;
    public const int QuantityDecimals = 8;
    public const int CostDecimals = 4;

    public const string ChangeMessage = "Only a draft migration batch can be changed.";
    public const string FundMessage = "Only a draft migration batch can be funded.";
    public const string UnfundSubmittedMessage = "Return the migration batch to draft first.";
    public const string UnfundPostedMessage = "The migration batch is posted, so this receipt cannot be taken out of it.";
    public const string FourEyesMessage =
        "A migration batch must be posted by someone other than the person who submitted it.";

    private readonly List<MigrationBatchEntry> _entries = [];
    private readonly List<MigrationBatchControlTotal> _controlTotalRows = [];

    private MigrationBatch()
    {
    }

    public Guid PublicId { get; private set; }

    public int TenantId { get; private set; }

    public MigrationBatchStatus Status { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string PreviousProvider { get; private set; } = string.Empty;

    public DateOnly AsOfDate { get; private set; }

    /// <summary>The control total of the number of accounts (R5).</summary>
    public int ControlAccountCount { get; private set; }

    public IReadOnlyList<MigrationBatchEntry> Entries => _entries;

    public IReadOnlyList<MigrationBatchControlTotal> ControlTotalRows => _controlTotalRows;

    public int? SubmittedByUserId { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public int? ReturnedByUserId { get; private set; }

    public DateTimeOffset? ReturnedAt { get; private set; }

    public string? ReturnNote { get; private set; }

    public int? PostedByUserId { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public int? CancelledByUserId { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancelNote { get; private set; }

    public byte[] RowVersion { get; private set; } = null!;

    /// <summary>The control totals as a person typed them (R5), read from the stored rows.</summary>
    public MigrationControlTotals ControlTotals => throw new NotImplementedException();

    /// <summary>R6: a new <c>Draft</c> batch with its header, control totals and entries (which may be empty).</summary>
    public static MigrationBatch Create(
        int tenantId,
        MigrationBatchHeader header,
        MigrationControlTotals controlTotals,
        IReadOnlyList<MigrationEntrySpec> entries) =>
        throw new NotImplementedException();

    /// <summary>R6: replaces the header, the control totals and every entry of a <c>Draft</c>.</summary>
    public void Replace(MigrationBatchHeader header, MigrationControlTotals controlTotals, IReadOnlyList<MigrationEntrySpec> entries) =>
        throw new NotImplementedException();

    /// <summary>R13: a funding line is added to the batch; only a <c>Draft</c> gains one. The row is updated so its
    /// <c>RowVersion</c> changes.</summary>
    public void AddFunding(DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R14: a funding line is taken out of the batch; only a <c>Draft</c> loses one.</summary>
    public void RemoveFunding(DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R17: <c>Draft</c> → <c>Submitted</c>. The caller has validated the batch.</summary>
    public void Submit(int byUserId, DateTimeOffset at) => throw new NotImplementedException();

    /// <summary>R18: <c>Submitted</c> → <c>Draft</c> with a note.</summary>
    public void Return(int byUserId, DateTimeOffset at, string? note) => throw new NotImplementedException();

    /// <summary>R19: <c>Draft</c> or <c>Submitted</c> → <c>Cancelled</c> with a note.</summary>
    public void Cancel(int byUserId, DateTimeOffset at, string? note) => throw new NotImplementedException();

    /// <summary>R20, R23: <c>Submitted</c> → <c>Posted</c>, by someone other than the submitter.</summary>
    public void MarkPosted(int byUserId, DateTimeOffset at) => throw new NotImplementedException();
}
