using MyWealthV2.Domain.ValueObjects;
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
    public MigrationControlTotals ControlTotals => new(
        ControlAccountCount,
        _controlTotalRows.Where(row => row.Kind == MigrationControlTotalKind.Cash)
            .Select(row => new MigrationCurrencyAmount(row.Currency!, row.Amount)).ToList(),
        _controlTotalRows.Where(row => row.Kind == MigrationControlTotalKind.Quantity)
            .Select(row => new MigrationInstrumentQuantity(row.InstrumentId!.Value, row.Amount)).ToList(),
        _controlTotalRows.Where(row => row.Kind == MigrationControlTotalKind.Cost)
            .Select(row => new MigrationCurrencyAmount(row.Currency!, row.Amount)).ToList());

    /// <summary>R6: a new <c>Draft</c> batch with its header, control totals and entries (which may be empty).</summary>
    public static MigrationBatch Create(
        int tenantId,
        MigrationBatchHeader header,
        MigrationControlTotals controlTotals,
        IReadOnlyList<MigrationEntrySpec> entries)
    {
        var batch = new MigrationBatch
        {
            TenantId = tenantId,
            PublicId = Guid.NewGuid(),
            Status = MigrationBatchStatus.Draft
        };
        batch.SetBody(header, controlTotals, entries);
        return batch;
    }

    /// <summary>R6: replaces the header, the control totals and every entry of a <c>Draft</c>.</summary>
    public void Replace(MigrationBatchHeader header, MigrationControlTotals controlTotals, IReadOnlyList<MigrationEntrySpec> entries)
    {
        if (Status != MigrationBatchStatus.Draft)
        {
            throw new MigrationBatchStatusException(ChangeMessage);
        }

        SetBody(header, controlTotals, entries);
    }

    /// <summary>R13: a funding line is added to the batch; only a <c>Draft</c> gains one. The row is updated so its
    /// <c>RowVersion</c> changes.</summary>
    public void AddFunding(DateTimeOffset at)
    {
        if (Status != MigrationBatchStatus.Draft)
        {
            throw new MigrationBatchStatusException(FundMessage);
        }

        LastModified = at;
    }

    /// <summary>R14: a funding line is taken out of the batch; only a <c>Draft</c> loses one.</summary>
    public void RemoveFunding(DateTimeOffset at)
    {
        switch (Status)
        {
            case MigrationBatchStatus.Submitted:
                throw new MigrationBatchStatusException(UnfundSubmittedMessage);
            case MigrationBatchStatus.Posted:
                throw new MigrationBatchStatusException(UnfundPostedMessage);
            case MigrationBatchStatus.Cancelled:
                throw new MigrationBatchStatusException(StatusMessage("changed"));
        }

        LastModified = at;
    }

    /// <summary>R17: <c>Draft</c> → <c>Submitted</c>. The caller has validated the batch.</summary>
    public void Submit(int byUserId, DateTimeOffset at)
    {
        EnsureStatus("submitted", MigrationBatchStatus.Draft);
        Status = MigrationBatchStatus.Submitted;
        SubmittedByUserId = byUserId;
        SubmittedAt = at;
    }

    /// <summary>R18: <c>Submitted</c> → <c>Draft</c> with a note.</summary>
    public void Return(int byUserId, DateTimeOffset at, string? note)
    {
        EnsureStatus("returned", MigrationBatchStatus.Submitted);
        var trimmed = RequiredNote(note);
        Status = MigrationBatchStatus.Draft;
        SubmittedByUserId = null;
        SubmittedAt = null;
        ReturnedByUserId = byUserId;
        ReturnedAt = at;
        ReturnNote = trimmed;
    }

    /// <summary>R19: <c>Draft</c> or <c>Submitted</c> → <c>Cancelled</c> with a note.</summary>
    public void Cancel(int byUserId, DateTimeOffset at, string? note)
    {
        EnsureStatus("cancelled", MigrationBatchStatus.Draft, MigrationBatchStatus.Submitted);
        var trimmed = RequiredNote(note);
        Status = MigrationBatchStatus.Cancelled;
        CancelledByUserId = byUserId;
        CancelledAt = at;
        CancelNote = trimmed;
    }

    /// <summary>R20, R23: <c>Submitted</c> → <c>Posted</c>, by someone other than the submitter.</summary>
    public void MarkPosted(int byUserId, DateTimeOffset at)
    {
        EnsureStatus("posted", MigrationBatchStatus.Submitted);
        EnsureNotSubmitter(byUserId);
        Status = MigrationBatchStatus.Posted;
        PostedByUserId = byUserId;
        PostedAt = at;
    }

    /// <summary>R20: the person who submitted the batch may not post it.</summary>
    public void EnsureNotSubmitter(int byUserId)
    {
        if (byUserId == SubmittedByUserId)
        {
            throw new MigrationBatchStatusException(FourEyesMessage);
        }
    }

    /// <summary>The sentence of a move the status does not allow (R17, R20).</summary>
    public string StatusMessage(string action) => $"This migration batch is {Status} and cannot be {action}.";

    private void EnsureStatus(string action, params MigrationBatchStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new MigrationBatchStatusException(StatusMessage(action));
        }
    }

    private static string RequiredNote(string? note)
    {
        var trimmed = note?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NoteMaxLength)
        {
            throw new DomainException($"Note must be 1 to {NoteMaxLength} characters.");
        }

        return trimmed;
    }

    private void SetBody(MigrationBatchHeader header, MigrationControlTotals controlTotals, IReadOnlyList<MigrationEntrySpec> entries)
    {
        var name = RequiredText(header.Name, "Name");
        var provider = RequiredText(header.PreviousProvider, "Previous provider");
        var totals = TotalRows(controlTotals);
        var built = BuildEntries(entries);

        Name = name;
        PreviousProvider = provider;
        AsOfDate = header.AsOfDate;
        ControlAccountCount = controlTotals.AccountCount;
        _controlTotalRows.Clear();
        _controlTotalRows.AddRange(totals);
        _entries.Clear();
        _entries.AddRange(built);
    }

    private static string RequiredText(string? value, string field)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > TextMaxLength)
        {
            throw new DomainException($"{field} must be 1 to {TextMaxLength} characters.");
        }

        return trimmed;
    }

    private List<MigrationBatchControlTotal> TotalRows(MigrationControlTotals totals)
    {
        if (totals.AccountCount < 0)
        {
            throw new DomainException("The account count must be 0 or more.");
        }

        var rows = new List<MigrationBatchControlTotal>();
        rows.AddRange(CurrencyRows(totals.Cash, MigrationControlTotalKind.Cash));
        rows.AddRange(CurrencyRows(totals.Cost, MigrationControlTotalKind.Cost));
        var instruments = new HashSet<int>();
        foreach (var row in totals.Quantity)
        {
            if (!instruments.Add(row.InstrumentId))
            {
                throw new DomainException("Each instrument appears at most once in the quantity totals.");
            }

            EnsureNotNegative(row.Quantity);
            rows.Add(MigrationBatchControlTotal.Create(TenantId, MigrationControlTotalKind.Quantity, null, row.InstrumentId,
                row.Quantity));
        }

        return rows;
    }

    private IEnumerable<MigrationBatchControlTotal> CurrencyRows(
        IReadOnlyList<MigrationCurrencyAmount> amounts,
        MigrationControlTotalKind kind)
    {
        var currencies = new HashSet<string>();
        foreach (var row in amounts)
        {
            var currency = Money.Create(0m, row.Currency).Currency;
            if (!currencies.Add(currency))
            {
                throw new DomainException($"Each currency appears at most once in the {kind.ToString().ToLowerInvariant()} totals.");
            }

            EnsureNotNegative(row.Amount);
            yield return MigrationBatchControlTotal.Create(TenantId, kind, currency, null, row.Amount);
        }
    }

    private static void EnsureNotNegative(decimal value)
    {
        if (value < 0)
        {
            throw new DomainException("A control total must be 0 or more.");
        }
    }

    private List<MigrationBatchEntry> BuildEntries(IReadOnlyList<MigrationEntrySpec> entries)
    {
        if (entries.Count > MaxEntries)
        {
            throw new DomainException($"A migration batch has at most {MaxEntries} entries.");
        }

        if (entries.Sum(entry => entry.Holdings.Count) > MaxHoldings)
        {
            throw new DomainException($"A migration batch has at most {MaxHoldings} holdings.");
        }

        var accounts = new HashSet<int>();
        var built = new List<MigrationBatchEntry>(entries.Count);
        for (var position = 0; position < entries.Count; position++)
        {
            var entry = entries[position];
            if (!accounts.Add(entry.AccountId))
            {
                throw new DomainException("An account appears at most once in a migration batch.");
            }

            if (entry.Cash is null && entry.Holdings.Count == 0)
            {
                throw new DomainException("An entry has cash, at least one holding, or both.");
            }

            if (entry.Cash is { } cash && (cash <= 0 || DecimalPlaces(cash) > CashDecimals))
            {
                throw new DomainException($"Cash must be greater than 0 with at most {CashDecimals} decimal places.");
            }

            var reference = entry.ExternalReference?.Trim();
            if (string.IsNullOrEmpty(reference))
            {
                reference = null;
            }
            else if (reference.Length > TextMaxLength)
            {
                throw new DomainException($"External reference must be 1 to {TextMaxLength} characters.");
            }

            var instruments = new HashSet<int>();
            var holdings = new List<MigrationBatchHolding>(entry.Holdings.Count);
            foreach (var holding in entry.Holdings)
            {
                if (!instruments.Add(holding.InstrumentId))
                {
                    throw new DomainException("An instrument appears at most once in an entry.");
                }

                if (holding.Quantity <= 0 || DecimalPlaces(holding.Quantity) > QuantityDecimals)
                {
                    throw new DomainException(
                        $"Quantity must be greater than 0 with at most {QuantityDecimals} decimal places.");
                }

                if (holding.Cost < 0 || DecimalPlaces(holding.Cost) > CostDecimals)
                {
                    throw new DomainException($"Cost must be 0 or more with at most {CostDecimals} decimal places.");
                }

                holdings.Add(MigrationBatchHolding.Create(TenantId, holding.InstrumentId, holding.Quantity, holding.Cost));
            }

            built.Add(MigrationBatchEntry.Create(TenantId, position, entry.AccountId, reference, entry.Cash, holdings));
        }

        return built;
    }

    /// <summary>The number of decimal places of <paramref name="value"/> without its trailing zeros.</summary>
    public static int DecimalPlaces(decimal value)
    {
        var normalised = value / 1.0000000000000000000000000000m;
        return (decimal.GetBits(normalised)[3] >> 16) & 0xFF;
    }
}
