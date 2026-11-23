using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Migrations;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Migrations;

/// <summary>Migration batches R3–R6, R17–R20: the shape of the body, its limits, the transitions and four eyes.</summary>
public class MigrationBatchTests
{
    private const int TenantId = 3;
    private const int Submitter = 21;
    private const int Poster = 22;
    private static readonly DateTimeOffset At = new(2026, 11, 24, 1, 0, 0, TimeSpan.Zero);
    private static readonly MigrationBatchHeader Header = new("  Acme transfer  ", " Acme Wealth ", new DateOnly(2026, 11, 20));

    private static readonly MigrationControlTotals Totals = new(
        2,
        [new MigrationCurrencyAmount("USD", 250000m)],
        [new MigrationInstrumentQuantity(9, 1200m)],
        [new MigrationCurrencyAmount("USD", 264000m)]);

    private static MigrationEntrySpec Entry(int accountId, decimal? cash = 100m, params MigrationHoldingSpec[] holdings) =>
        new(accountId, " ACME-1 ", cash, holdings);

    private static MigrationHoldingSpec Holding(int instrumentId = 9, decimal quantity = 800m, decimal cost = 176000m) =>
        new(instrumentId, quantity, cost);

    private static MigrationBatch Draft(params MigrationEntrySpec[] entries) =>
        MigrationBatch.Create(TenantId, Header, Totals, entries.Length == 0 ? [Entry(5, 150000m, Holding())] : entries);

    private static MigrationBatch Submitted()
    {
        var batch = Draft();
        batch.Submit(Submitter, At);
        return batch;
    }

    [Test]
    public void Create_IsADraft_WithTheTrimmedHeader_TheControlTotals_AndTheEntriesInOrder()
    {
        var batch = MigrationBatch.Create(TenantId, Header, Totals,
            [Entry(5, 150000m, Holding()), Entry(6, null, Holding(9, 400m, 88000m), Holding(10, 1m, 0m))]);

        batch.Status.ShouldBe(MigrationBatchStatus.Draft);
        batch.PublicId.ShouldNotBe(Guid.Empty);
        (batch.TenantId, batch.Name, batch.PreviousProvider, batch.AsOfDate)
            .ShouldBe((TenantId, "Acme transfer", "Acme Wealth", new DateOnly(2026, 11, 20)));
        batch.ControlAccountCount.ShouldBe(2);
        batch.ControlTotals.Cash.ShouldBe([new MigrationCurrencyAmount("USD", 250000m)]);
        batch.ControlTotals.Quantity.ShouldBe([new MigrationInstrumentQuantity(9, 1200m)]);
        batch.ControlTotals.Cost.ShouldBe([new MigrationCurrencyAmount("USD", 264000m)]);
        batch.Entries.Select(e => (e.Position, e.AccountId, e.ExternalReference, e.Cash))
            .ShouldBe([(0, 5, "ACME-1", (decimal?)150000m), (1, 6, "ACME-1", (decimal?)null)]);
        batch.Entries[1].Holdings.Select(h => (h.InstrumentId, h.Quantity, h.Cost)).ShouldBe([(9, 400m, 88000m), (10, 1m, 0m)]);
        batch.Entries.All(e => e.TenantId == TenantId && e.Holdings.All(h => h.TenantId == TenantId)).ShouldBeTrue();
    }

    [Test]
    public void Create_WithNoEntries_IsADraft()
    {
        MigrationBatch.Create(TenantId, Header, MigrationControlTotals.Empty, []).Entries.ShouldBeEmpty();
    }

    [Test]
    public void Create_ABlankExternalReference_IsNull()
    {
        var batch = MigrationBatch.Create(TenantId, Header, Totals, [new MigrationEntrySpec(5, "  ", 1m, [])]);

        batch.Entries.Single().ExternalReference.ShouldBeNull();
    }

    private static IEnumerable<TestCaseData> BadBodies()
    {
        yield return new TestCaseData(new Func<MigrationBatch>(() => Draft(new MigrationEntrySpec(5, null, null, []))))
            .SetArgDisplayNames("neither cash nor a holding");
        yield return new TestCaseData(new Func<MigrationBatch>(() => Draft(Entry(5), Entry(5))))
            .SetArgDisplayNames("an Account twice");
        yield return new TestCaseData(new Func<MigrationBatch>(() => Draft(Entry(5, 1m, Holding(9), Holding(9)))))
            .SetArgDisplayNames("an Instrument twice in an entry");
        yield return new TestCaseData(new Func<MigrationBatch>(() => Draft(Entry(5, 0m))))
            .SetArgDisplayNames("cash 0");
        yield return new TestCaseData(new Func<MigrationBatch>(() => Draft(Entry(5, 1.00001m))))
            .SetArgDisplayNames("cash with 5 decimals");
        yield return new TestCaseData(new Func<MigrationBatch>(() => Draft(Entry(5, null, Holding(quantity: 0m)))))
            .SetArgDisplayNames("quantity 0");
        yield return new TestCaseData(new Func<MigrationBatch>(() => Draft(Entry(5, null, Holding(quantity: 0.000000001m)))))
            .SetArgDisplayNames("quantity with 9 decimals");
        yield return new TestCaseData(new Func<MigrationBatch>(() => Draft(Entry(5, null, Holding(cost: -0.01m)))))
            .SetArgDisplayNames("cost below 0");
        yield return new TestCaseData(new Func<MigrationBatch>(() => Draft(new MigrationEntrySpec(5, new string('x', 101), 1m, []))))
            .SetArgDisplayNames("an external reference of 101 characters");
        yield return new TestCaseData(new Func<MigrationBatch>(() =>
                MigrationBatch.Create(TenantId, Header, Totals, Enumerable.Range(1, 501).Select(id => Entry(id)).ToList())))
            .SetArgDisplayNames("501 entries");
        yield return new TestCaseData(new Func<MigrationBatch>(() =>
                MigrationBatch.Create(TenantId, Header, Totals,
                    Enumerable.Range(1, 5).Select(id => Entry(id, null,
                        Enumerable.Range(1, id == 5 ? 1001 : 1000).Select(i => Holding(i, 1m, 1m)).ToArray())).ToList())))
            .SetArgDisplayNames("5 001 holdings");
        yield return new TestCaseData(new Func<MigrationBatch>(() =>
                MigrationBatch.Create(TenantId, Header with { Name = " " }, Totals, [])))
            .SetArgDisplayNames("an empty name");
        yield return new TestCaseData(new Func<MigrationBatch>(() =>
                MigrationBatch.Create(TenantId, Header with { PreviousProvider = new string('p', 101) }, Totals, [])))
            .SetArgDisplayNames("a provider of 101 characters");
        yield return new TestCaseData(new Func<MigrationBatch>(() =>
                MigrationBatch.Create(TenantId, Header, Totals with { AccountCount = -1 }, [])))
            .SetArgDisplayNames("an account count below 0");
        yield return new TestCaseData(new Func<MigrationBatch>(() =>
                MigrationBatch.Create(TenantId, Header,
                    Totals with { Cash = [new MigrationCurrencyAmount("USD", 1m), new MigrationCurrencyAmount("usd", 2m)] }, [])))
            .SetArgDisplayNames("a currency twice in the cash totals");
        yield return new TestCaseData(new Func<MigrationBatch>(() =>
                MigrationBatch.Create(TenantId, Header,
                    Totals with { Quantity = [new MigrationInstrumentQuantity(9, 1m), new MigrationInstrumentQuantity(9, 2m)] }, [])))
            .SetArgDisplayNames("an Instrument twice in the quantity totals");
        yield return new TestCaseData(new Func<MigrationBatch>(() =>
                MigrationBatch.Create(TenantId, Header, Totals with { Cost = [new MigrationCurrencyAmount("USD", -1m)] }, [])))
            .SetArgDisplayNames("a cost total below 0");
    }

    [TestCaseSource(nameof(BadBodies))]
    public void Create_ABadBody_Throws(Func<MigrationBatch> create)
    {
        Should.Throw<DomainException>(create);
    }

    [Test]
    public void Create_500Entries_And5000Holdings_IsAllowed()
    {
        var entries = Enumerable.Range(1, 500)
            .Select(id => Entry(id, null, Enumerable.Range(1, 10).Select(i => Holding(i, 1m, 1m)).ToArray()))
            .ToList();

        MigrationBatch.Create(TenantId, Header, Totals, entries).Entries.Sum(e => e.Holdings.Count).ShouldBe(5000);
    }

    [Test]
    public void Replace_OnADraft_ReplacesTheHeader_TheControlTotals_AndEveryEntry()
    {
        var batch = Draft(Entry(5), Entry(6));

        batch.Replace(new MigrationBatchHeader("Second", "Beta", new DateOnly(2026, 11, 21)),
            MigrationControlTotals.Empty with { AccountCount = 1 }, [Entry(7, 5m)]);

        (batch.Name, batch.PreviousProvider, batch.AsOfDate).ShouldBe(("Second", "Beta", new DateOnly(2026, 11, 21)));
        batch.ControlAccountCount.ShouldBe(1);
        batch.ControlTotals.Cash.ShouldBeEmpty();
        batch.Entries.Select(e => (e.Position, e.AccountId, e.Cash)).ShouldBe([(0, 7, (decimal?)5m)]);
    }

    [Test]
    public void Replace_OnASubmittedBatch_Throws_WithTheR6Message()
    {
        var batch = Submitted();

        Should.Throw<MigrationBatchStatusException>(() => batch.Replace(Header, Totals, [Entry(5)]))
            .Message.ShouldBe("Only a draft migration batch can be changed.");
    }

    [Test]
    public void AddAndRemoveFunding_OnADraft_TouchTheRow()
    {
        var batch = Draft();

        batch.AddFunding(At);
        batch.LastModified.ShouldBe(At);
        batch.RemoveFunding(At.AddMinutes(1));
        batch.LastModified.ShouldBe(At.AddMinutes(1));
    }

    [Test]
    public void AddFunding_ToABatchThatIsNotADraft_Throws()
    {
        Should.Throw<MigrationBatchStatusException>(() => Submitted().AddFunding(At))
            .Message.ShouldBe("Only a draft migration batch can be funded.");
    }

    [Test]
    public void RemoveFunding_OfASubmittedOrPostedBatch_Throws_WithTheR14Sentences()
    {
        Should.Throw<MigrationBatchStatusException>(() => Submitted().RemoveFunding(At))
            .Message.ShouldBe("Return the migration batch to draft first.");

        var posted = Submitted();
        posted.MarkPosted(Poster, At);
        Should.Throw<MigrationBatchStatusException>(() => posted.RemoveFunding(At))
            .Message.ShouldBe("The migration batch is posted, so this receipt cannot be taken out of it.");
    }

    [Test]
    public void Submit_FromDraft_RecordsThePersonAndTime()
    {
        var batch = Submitted();

        batch.Status.ShouldBe(MigrationBatchStatus.Submitted);
        (batch.SubmittedByUserId, batch.SubmittedAt).ShouldBe(((int?)Submitter, (DateTimeOffset?)At));
    }

    [Test]
    public void Submit_FromSubmitted_Throws_WithTheStatusSentence()
    {
        Should.Throw<MigrationBatchStatusException>(() => Submitted().Submit(Submitter, At))
            .Message.ShouldBe("This migration batch is Submitted and cannot be submitted.");
    }

    [Test]
    public void Return_FromSubmitted_IsADraftAgain_WithTheReturn_AndNoSubmitter()
    {
        var batch = Submitted();

        batch.Return(Submitter, At.AddHours(1), "  Wrong totals ");

        batch.Status.ShouldBe(MigrationBatchStatus.Draft);
        (batch.ReturnedByUserId, batch.ReturnedAt, batch.ReturnNote)
            .ShouldBe(((int?)Submitter, (DateTimeOffset?)At.AddHours(1), "Wrong totals"));
        (batch.SubmittedByUserId, batch.SubmittedAt).ShouldBe(((int?)null, (DateTimeOffset?)null));
    }

    [TestCase(null)]
    [TestCase("   ")]
    public void Return_WithoutANote_Throws(string? note)
    {
        Should.Throw<DomainException>(() => Submitted().Return(Poster, At, note));
    }

    [Test]
    public void Return_ANoteOf201Characters_Throws()
    {
        Should.Throw<DomainException>(() => Submitted().Return(Poster, At, new string('n', 201)));
    }

    [Test]
    public void Return_FromDraft_Throws()
    {
        Should.Throw<MigrationBatchStatusException>(() => Draft().Return(Poster, At, "note"))
            .Message.ShouldBe("This migration batch is Draft and cannot be returned.");
    }

    [Test]
    public void Cancel_FromDraftOrSubmitted_IsCancelled_WithTheNote()
    {
        var draft = Draft();
        draft.Cancel(Submitter, At, " Duplicate ");
        draft.Status.ShouldBe(MigrationBatchStatus.Cancelled);
        (draft.CancelledByUserId, draft.CancelledAt, draft.CancelNote).ShouldBe(((int?)Submitter, (DateTimeOffset?)At, "Duplicate"));

        var submitted = Submitted();
        submitted.Cancel(Poster, At, "Duplicate");
        submitted.Status.ShouldBe(MigrationBatchStatus.Cancelled);
        submitted.SubmittedByUserId.ShouldBe(Submitter);
    }

    [Test]
    public void Cancel_WithoutANote_OrWhenFinal_Throws()
    {
        Should.Throw<DomainException>(() => Draft().Cancel(Poster, At, ""));

        var cancelled = Draft();
        cancelled.Cancel(Poster, At, "Duplicate");
        Should.Throw<MigrationBatchStatusException>(() => cancelled.Cancel(Poster, At, "Again"))
            .Message.ShouldBe("This migration batch is Cancelled and cannot be cancelled.");
    }

    [Test]
    public void MarkPosted_ByASecondPerson_IsPosted()
    {
        var batch = Submitted();

        batch.MarkPosted(Poster, At.AddHours(2));

        batch.Status.ShouldBe(MigrationBatchStatus.Posted);
        (batch.PostedByUserId, batch.PostedAt).ShouldBe(((int?)Poster, (DateTimeOffset?)At.AddHours(2)));
    }

    [Test]
    public void MarkPosted_ByTheSubmitter_Throws_WithTheFourEyesSentence()
    {
        var batch = Submitted();

        Should.Throw<MigrationBatchStatusException>(() => batch.MarkPosted(Submitter, At))
            .Message.ShouldBe("A migration batch must be posted by someone other than the person who submitted it.");
        batch.Status.ShouldBe(MigrationBatchStatus.Submitted);
    }

    [Test]
    public void MarkPosted_FromDraft_Throws_WithTheStatusSentence()
    {
        Should.Throw<MigrationBatchStatusException>(() => Draft().MarkPosted(Poster, At))
            .Message.ShouldBe("This migration batch is Draft and cannot be posted.");
    }

    [Test]
    public void RecordTransactions_StoresTheIds_Once()
    {
        var entry = Draft().Entries.Single();

        entry.RecordTransactions(41, 42);

        (entry.OpeningTransactionId, entry.OpeningHoldingsTransactionId).ShouldBe(((int?)41, (int?)42));
        Should.Throw<DomainException>(() => entry.RecordTransactions(43, null));
    }
}
