using System.Globalization;

namespace MyWealthV2.Domain.Migrations;

/// <summary>The state of an entry's Account that the issues read (migration-batches R9, R10).</summary>
public sealed record MigrationAccountState(
    int AccountId,
    string Name,
    CustodyMode CustodyMode,
    AccountStatus Status,
    bool CustomerDisabled,
    bool HasTransactions,
    string Currency,
    int CurrencyDecimalPlaces);

/// <summary>The state of a holding's Instrument that the issues read (R9).</summary>
public sealed record MigrationInstrumentState(int InstrumentId, string Symbol, bool IsActive, string QuoteCurrency);

/// <summary>What <see cref="MigrationBatchValidation.Compute"/> reads: the batch's control totals and entries, the
/// state of their Accounts and Instruments, and the amount and currency of each funding line.</summary>
public sealed record MigrationValidationInputs(
    MigrationControlTotals ControlTotals,
    IReadOnlyList<MigrationEntrySpec> Entries,
    IReadOnlyDictionary<int, MigrationAccountState> Accounts,
    IReadOnlyDictionary<int, MigrationInstrumentState> Instruments,
    IReadOnlyList<MigrationCurrencyAmount> Funding);

/// <summary>One reason a batch is not valid (R9), with what it is about.</summary>
public sealed record MigrationIssue(
    MigrationIssueCode Code,
    int? AccountId,
    int? InstrumentId,
    string? Currency,
    MigrationTotal? Total,
    string Message);

/// <summary>The computed totals, the funding totals and the issues of a batch (R8, R9).</summary>
public sealed record MigrationValidationResult(
    MigrationControlTotals ComputedTotals,
    IReadOnlyList<MigrationCurrencyAmount> FundingTotals,
    IReadOnlyList<MigrationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// Migration batches R8, R9: a pure function over a batch and the state of its Accounts, Instruments and funding lines.
/// It decides the read, the submit and the post (R11).
/// </summary>
public static class MigrationBatchValidation
{
    private const string AmountFormat = "#,##0.00##";
    private const string QuantityFormat = "#,##0.########";

    public static MigrationValidationResult Compute(MigrationValidationInputs inputs)
    {
        var computed = Totals(inputs);
        var funding = FundingTotals(inputs, computed);
        var issues = new List<MigrationIssue>();
        if (inputs.Entries.Count == 0)
        {
            issues.Add(new MigrationIssue(MigrationIssueCode.NoEntries, null, null, null, null, "The batch has no entries."));
        }

        foreach (var entry in inputs.Entries)
        {
            if (inputs.Accounts.TryGetValue(entry.AccountId, out var account))
            {
                issues.AddRange(EntryIssues(entry, account, inputs.Instruments));
            }
        }

        issues.AddRange(TotalIssues(inputs.ControlTotals, computed, inputs.Instruments));
        foreach (var total in funding)
        {
            var cash = Find(computed.Cash, total.Currency);
            if (total.Amount != cash)
            {
                issues.Add(new MigrationIssue(MigrationIssueCode.FundingMismatch, null, null, total.Currency, null,
                    $"Bank receipts for this batch add up to {Amount(total.Amount)} {total.Currency}; its cash is {Amount(cash)} {total.Currency}."));
            }
        }

        return new MigrationValidationResult(computed, funding, issues);
    }

    private static IEnumerable<MigrationIssue> EntryIssues(
        MigrationEntrySpec entry,
        MigrationAccountState account,
        IReadOnlyDictionary<int, MigrationInstrumentState> instruments)
    {
        if (account.CustodyMode != CustodyMode.Custodial)
        {
            yield return AboutEntry(MigrationIssueCode.AccountNotCustodial, account,
                $"{account.Name} is an External account. A migration batch fills Custodial accounts only.");
        }

        if (account.Status != AccountStatus.Open)
        {
            yield return AboutEntry(MigrationIssueCode.AccountNotOpen, account, $"{account.Name} is closed.");
        }

        if (account.CustomerDisabled)
        {
            yield return AboutEntry(MigrationIssueCode.CustomerDisabled, account, $"The customer of {account.Name} is disabled.");
        }

        if (account.HasTransactions)
        {
            yield return AboutEntry(MigrationIssueCode.AccountNotEmpty, account,
                $"{account.Name} already has transactions. A migration batch fills empty accounts only.");
        }

        if (entry.Cash is { } cash && Entities.MigrationBatch.DecimalPlaces(cash) > account.CurrencyDecimalPlaces)
        {
            yield return AboutEntry(MigrationIssueCode.CashPrecision, account,
                $"The cash of {account.Name} has more than {account.CurrencyDecimalPlaces} decimal places for {account.Currency}.");
        }

        foreach (var holding in entry.Holdings)
        {
            if (!instruments.TryGetValue(holding.InstrumentId, out var instrument))
            {
                continue;
            }

            if (!instrument.IsActive)
            {
                yield return new MigrationIssue(MigrationIssueCode.InstrumentInactive, account.AccountId, instrument.InstrumentId,
                    null, null, $"{instrument.Symbol} is disabled.");
            }

            if (!string.Equals(instrument.QuoteCurrency, account.Currency, StringComparison.Ordinal))
            {
                yield return new MigrationIssue(MigrationIssueCode.InstrumentCurrencyMismatch, account.AccountId,
                    instrument.InstrumentId, null, null,
                    $"{instrument.Symbol} is quoted in {instrument.QuoteCurrency}; {account.Name} is in {account.Currency}.");
            }
        }
    }

    private static MigrationIssue AboutEntry(MigrationIssueCode code, MigrationAccountState account, string message) =>
        new(code, account.AccountId, null, null, null, message);

    private static IEnumerable<MigrationIssue> TotalIssues(
        MigrationControlTotals control,
        MigrationControlTotals computed,
        IReadOnlyDictionary<int, MigrationInstrumentState> instruments)
    {
        if (control.AccountCount != computed.AccountCount)
        {
            yield return new MigrationIssue(MigrationIssueCode.ControlTotalMismatch, null, null, null, MigrationTotal.AccountCount,
                $"The control total of accounts is {control.AccountCount}; the batch has {computed.AccountCount} entries.");
        }

        foreach (var issue in CurrencyIssues(MigrationTotal.Cash, "cash", control.Cash, computed.Cash))
        {
            yield return issue;
        }

        var quantityKeys = computed.Quantity.Select(q => q.InstrumentId)
            .Concat(control.Quantity.Select(q => q.InstrumentId)).Distinct();
        foreach (var instrumentId in quantityKeys)
        {
            var symbol = instruments.TryGetValue(instrumentId, out var instrument) ? instrument.Symbol : $"Instrument {instrumentId}";
            var typed = control.Quantity.FirstOrDefault(q => q.InstrumentId == instrumentId)?.Quantity;
            var held = computed.Quantity.FirstOrDefault(q => q.InstrumentId == instrumentId)?.Quantity;
            var message = (typed, held) switch
            {
                (null, { } h) => $"{symbol} has no control total; the entries hold {Quantity(h)}.",
                ({ } t, null) when t != 0 => $"The control total of {symbol} is {Quantity(t)}; the entries hold none.",
                ({ } t, { } h) when t != h => $"The control total of {symbol} is {Quantity(t)}; the entries hold {Quantity(h)}.",
                _ => null
            };
            if (message is not null)
            {
                yield return new MigrationIssue(MigrationIssueCode.ControlTotalMismatch, null, instrumentId, null,
                    MigrationTotal.Quantity, message);
            }
        }

        foreach (var issue in CurrencyIssues(MigrationTotal.Cost, "cost", control.Cost, computed.Cost))
        {
            yield return issue;
        }
    }

    private static IEnumerable<MigrationIssue> CurrencyIssues(
        MigrationTotal total,
        string name,
        IReadOnlyList<MigrationCurrencyAmount> control,
        IReadOnlyList<MigrationCurrencyAmount> computed)
    {
        var currencies = computed.Select(c => c.Currency).Concat(control.Select(c => c.Currency)).Distinct(StringComparer.Ordinal);
        foreach (var currency in currencies)
        {
            var typed = control.FirstOrDefault(c => c.Currency == currency)?.Amount;
            var sum = computed.FirstOrDefault(c => c.Currency == currency)?.Amount;
            var label = char.ToUpperInvariant(name[0]) + name[1..];
            var message = (typed, sum) switch
            {
                (null, { } s) => $"{label} in {currency} has no control total; the entries add up to {Amount(s)}.",
                ({ } t, null) when t != 0 => $"The control total of {name} in {currency} is {Amount(t)}; the entries have none.",
                ({ } t, { } s) when t != s => $"The control total of {name} in {currency} is {Amount(t)}; the entries add up to {Amount(s)}.",
                _ => null
            };
            if (message is not null)
            {
                yield return new MigrationIssue(MigrationIssueCode.ControlTotalMismatch, null, null, currency, total, message);
            }
        }
    }

    private static MigrationControlTotals Totals(MigrationValidationInputs inputs)
    {
        var cash = new List<MigrationCurrencyAmount>();
        var quantity = new List<MigrationInstrumentQuantity>();
        var cost = new List<MigrationCurrencyAmount>();
        foreach (var entry in inputs.Entries)
        {
            if (entry.Cash is { } amount && inputs.Accounts.TryGetValue(entry.AccountId, out var account))
            {
                Add(cash, account.Currency, amount);
            }

            foreach (var holding in entry.Holdings)
            {
                var index = quantity.FindIndex(q => q.InstrumentId == holding.InstrumentId);
                if (index < 0)
                {
                    quantity.Add(new MigrationInstrumentQuantity(holding.InstrumentId, holding.Quantity));
                }
                else
                {
                    quantity[index] = quantity[index] with { Quantity = quantity[index].Quantity + holding.Quantity };
                }

                if (inputs.Instruments.TryGetValue(holding.InstrumentId, out var instrument))
                {
                    Add(cost, instrument.QuoteCurrency, holding.Cost);
                }
            }
        }

        return new MigrationControlTotals(inputs.Entries.Count, cash, quantity, cost);
    }

    private static List<MigrationCurrencyAmount> FundingTotals(MigrationValidationInputs inputs, MigrationControlTotals computed)
    {
        var totals = computed.Cash.Select(c => new MigrationCurrencyAmount(c.Currency, 0m)).ToList();
        foreach (var line in inputs.Funding)
        {
            Add(totals, line.Currency, line.Amount);
        }

        return totals;
    }

    private static void Add(List<MigrationCurrencyAmount> totals, string currency, decimal amount)
    {
        var index = totals.FindIndex(t => t.Currency == currency);
        if (index < 0)
        {
            totals.Add(new MigrationCurrencyAmount(currency, amount));
        }
        else
        {
            totals[index] = totals[index] with { Amount = totals[index].Amount + amount };
        }
    }

    private static decimal Find(IReadOnlyList<MigrationCurrencyAmount> totals, string currency) =>
        totals.FirstOrDefault(t => t.Currency == currency)?.Amount ?? 0m;

    private static string Amount(decimal value) => value.ToString(AmountFormat, CultureInfo.InvariantCulture);

    private static string Quantity(decimal value) => value.ToString(QuantityFormat, CultureInfo.InvariantCulture);
}
