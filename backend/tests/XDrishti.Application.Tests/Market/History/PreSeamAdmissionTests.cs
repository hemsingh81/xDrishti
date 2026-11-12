#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.History;
using XDrishti.Application.Research;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.History;

namespace XDrishti.Application.Tests.Market.History;

/// <summary>
/// CF-04d acceptance criteria 1 to 5 on fakes: the admission rule applied to the stored facts, the shared read (<see cref="SeamAwareBars.ReadForRunAsync"/>) and the run reader. Hand instrument X: split 1:5 (0.2) ex 2023-03-10, bonus 1:1 (0.5) ex 2024-08-01, seam 2024-04-02 09:15 IST,
/// <c>csvAdjustedThrough</c> 2026-04-30.
/// </summary>
public sealed class PreSeamAdmissionTests
{
    private static readonly DateTimeOffset Seam = new(2024, 4, 2, 9, 15, 0, IndiaTime.Offset);

    private static readonly DateOnly Through = new(2026, 4, 30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AppliedAction Split(string exDate, int n, int d) => new(DateOnly.Parse(exDate, System.Globalization.CultureInfo.InvariantCulture), CorporateActionKind.Split, n, d, CorporateActionRatio.Factor(CorporateActionKind.Split, n, d));

    private static AppliedAction Bonus(string exDate) => new(DateOnly.Parse(exDate, System.Globalization.CultureInfo.InvariantCulture), CorporateActionKind.Bonus, 1, 1, CorporateActionRatio.Factor(CorporateActionKind.Bonus, 1, 1));

    private static Bar Dhan(DateTimeOffset at, double close, long volume) => new(at.ToUnixTimeSeconds(), close, close, close, close, volume);

    private static AdjustmentFactors X(params AppliedAction[] extra) => AdjustmentFactors.From([Split("2023-03-10", 1, 5), Bonus("2024-08-01"), .. extra]);

    private static SeamRecord Record(Guid id, AdjustmentFactors factors, string probe = ProbeStatus.Passed, bool jump = false, bool continuity = true) =>
        new(id, Seam, probe, null, 1, 100, null, jump, 0.84, SeamBasis.Of(Seam, factors.Actions, Through), continuity ? SeamBasis.Of(Seam, factors.Actions, Through) : null);

    private static SeamAdmissionFacts Facts(SeamRecord record, DateTimeOffset? current = null, DateTimeOffset? highWater = null, bool unfinished = false) =>
        new(true, record, current ?? Seam, highWater ?? Seam, unfinished);

    private static async ValueTask<PreSeamDecision> DecideAsync(FakeSeams seams, Guid id, AdjustmentFactors factors, DateOnly? through = null) =>
        await PreSeamAdmission.DecideAsync(seams, id, factors, through ?? Through, false, Ct);

    [Fact]
    public async Task A_verified_hand_instrument_is_admitted_with_its_seam_date_and_basis()
    {
        var id = Guid.NewGuid();
        var factors = X();
        var seams = new FakeSeams();
        seams.Facts[id] = Facts(Record(id, factors));

        var decision = await DecideAsync(seams, id, factors);

        decision.Reason.ShouldBeNull();
        decision.Admitted.ShouldBe(new XDrishti.Domain.Research.PreSeamRecord(new DateOnly(2024, 4, 2), SeamBasis.Of(Seam, factors.Actions, Through)));
    }

    [Fact]
    public async Task With_the_switch_off_nothing_is_admitted_or_counted()
    {
        var id = Guid.NewGuid();

        var decision = await DecideAsync(new FakeSeams(), id, X());

        decision.HasSeamRow.ShouldBeFalse();
        decision.IsAdmitted.ShouldBeFalse();
        decision.Reason.ShouldBe(SeamVerification.SwitchOff);
    }

    [Fact]
    public async Task An_instrument_without_a_seam_row_is_not_applicable_and_not_counted()
    {
        var id = Guid.NewGuid();
        var seams = new FakeSeams();
        seams.Facts[id] = new SeamAdmissionFacts(true, null, null, null, false);

        var decision = await DecideAsync(seams, id, X());

        decision.HasSeamRow.ShouldBeFalse();
        decision.Reason.ShouldBe(PreSeamAdmission.NoSeam);
    }

    [Fact]
    public async Task A_split_added_after_the_probe_reads_basis_changed_and_a_csv_end_that_moves_without_crossing_an_ex_date_stays_verified()
    {
        var id = Guid.NewGuid();
        var seams = new FakeSeams();
        seams.Facts[id] = Facts(Record(id, X()));

        var added = await DecideAsync(seams, id, X(Split("2026-06-15", 1, 2)));
        var moved = await DecideAsync(seams, id, X(), new DateOnly(2026, 5, 29));
        var crossing = await DecideAsync(seams, id, X(Split("2026-06-15", 1, 2)), new DateOnly(2026, 6, 30));

        added.Reason.ShouldBe(SeamVerification.BasisChanged);
        moved.Reason.ShouldBeNull();
        crossing.Reason.ShouldBe(SeamVerification.BasisChanged);
    }

    [Fact]
    public async Task A_moved_earlier_seam_reads_seam_moved_and_a_passing_re_probe_does_not_admit_it()
    {
        var id = Guid.NewGuid();
        var factors = X();
        var earlier = Seam.AddDays(-34);
        var seams = new FakeSeams();
        // the seam row was re-probed on the new seam (record = new seam, passed, both bases current) but the high-water mark stays at the old one
        seams.Facts[id] = new SeamAdmissionFacts(true, Record(id, factors) with { SeamAt = earlier, ProbeBasis = SeamBasis.Of(earlier, factors.Actions, Through), ContinuityBasis = SeamBasis.Of(earlier, factors.Actions, Through) }, earlier, Seam, false);

        (await DecideAsync(seams, id, factors)).Reason.ShouldBe(SeamVerification.SeamMoved);
    }

    [Fact]
    public async Task A_dhan_repair_bar_between_the_seam_and_the_last_in_span_ex_date_reads_dhan_in_csv_span_and_one_after_it_does_not_matter()
    {
        var id = Guid.NewGuid();
        var factors = X(Split("2025-01-10", 1, 2));
        var seams = new FakeSeams();
        seams.Facts[id] = Facts(Record(id, factors));
        seams.DhanInSpan.Add(id);

        (await DecideAsync(seams, id, factors)).Reason.ShouldBe(SeamVerification.DhanInCsvSpan);
        (await DecideAsync(seams, id, X(), null)).Reason.ShouldBe(SeamVerification.BasisChanged);   // other basis: the earlier reasons win
        // no action inside the span (every action before the seam or after the CSV end): no scan is needed and a dhan bar cannot matter
        var plain = AdjustmentFactors.From([Split("2023-03-10", 1, 5)]);
        seams.Facts[id] = Facts(Record(id, plain));
        (await DecideAsync(seams, id, plain)).Reason.ShouldBeNull();
    }

    [Fact]
    public async Task The_read_of_an_admitted_instrument_feeds_the_raw_pre_seam_bars_with_the_source_aware_factors_and_the_hand_prices()
    {
        var id = Guid.NewGuid();
        var factors = X();
        var seams = new FakeSeams();
        seams.Cuts[id] = Seam;
        seams.Facts[id] = Facts(Record(id, factors));
        var store = new FakeBarStore();
        var december = new DateTimeOffset(2022, 12, 1, 9, 16, 0, IndiaTime.Offset);
        var lastDhan = new DateTimeOffset(2024, 4, 1, 15, 29, 0, IndiaTime.Offset);
        store.Stored[id] = [Dhan(december, 2500.00, 1_000), Dhan(lastDhan, 500.00, 1_000), Dhan(Seam, 252.10, 10), Dhan(new DateTimeOffset(2024, 5, 2, 9, 15, 0, IndiaTime.Offset), 260.00, 10)];

        var read = await SeamAwareBars.ReadForRunAsync(store, seams, id, factors, Through, DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow, Ct);

        read.Decision.IsAdmitted.ShouldBeTrue();
        read.Bars.Count.ShouldBe(4);
        AdjustmentFactors.Price(2500.00, read.Factors.FactorAt(new DateOnly(2022, 12, 1))).ShouldBe(250.00);
        AdjustmentFactors.Volume(1_000, read.Factors.FactorAt(new DateOnly(2022, 12, 1))).ShouldBe(10_000);
        AdjustmentFactors.Price(500.00, read.Factors.FactorAt(new DateOnly(2024, 4, 1))).ShouldBe(250.00);
        AdjustmentFactors.Price(252.10, read.Factors.FactorAt(new DateOnly(2024, 4, 2))).ShouldBe(252.10);
        AdjustmentFactors.Price(260.00, read.Factors.FactorAt(new DateOnly(2024, 5, 2))).ShouldBe(260.00);
    }

    [Fact]
    public async Task The_read_of_an_instrument_that_is_not_admitted_is_the_cut_bars_with_the_plain_factors()
    {
        var id = Guid.NewGuid();
        var factors = X();
        var seams = new FakeSeams();
        seams.Cuts[id] = Seam;
        seams.Facts[id] = Facts(Record(id, factors, probe: ProbeStatus.None));
        var store = new FakeBarStore();
        store.Stored[id] = [Dhan(new DateTimeOffset(2022, 12, 1, 9, 16, 0, IndiaTime.Offset), 2500.00, 1_000), Dhan(Seam, 252.10, 10)];

        var read = await SeamAwareBars.ReadForRunAsync(store, seams, id, factors, Through, DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow, Ct);

        read.Decision.Reason.ShouldBe(SeamVerification.NotProbed);
        read.Bars.Select(b => b.EpochSeconds).ShouldBe([Seam.ToUnixTimeSeconds()]);
        read.Factors.ShouldBeSameAs(factors);
    }

    [Fact]
    public async Task The_run_reader_gives_an_admitted_instrument_the_preseam_record_and_never_csv_adjusted_twice_with_or_without_ack()
    {
        var id = Guid.NewGuid();
        var factors = X();
        var seams = new FakeSeams();
        seams.Cuts[id] = Seam;
        seams.Facts[id] = Facts(Record(id, factors));
        var store = new FakeBarStore();
        store.Stored[id] = [Dhan(new DateTimeOffset(2022, 12, 1, 9, 16, 0, IndiaTime.Offset), 2500.00, 1_000), Dhan(Seam, 252.10, 10)];
        var adjustments = Substitute.For<IAdjustmentFactors>();
        adjustments.GetAsync(id, Arg.Any<CancellationToken>()).Returns(factors);
        var reader = new InstrumentInputReader(store, Substitute.For<IBarQueries>(), adjustments, seams);
        var instrument = new BarsInstrument(id, "X", "X Ltd", 5);
        var csvThrough = new Dictionary<Guid, DateOnly> { [id] = Through };

        var read = await reader.ReadAsync(id, new DateOnly(2026, 4, 30), Through, Ct);

        foreach (var ack in new[] { false, true })
        {
            var input = InstrumentInputReader.Build(id, instrument, read.Bars, read.Factors, ack, new(), csvThrough, XDrishti.Domain.Research.ReplayOptions.Default, new DateOnly(2023, 1, 1), new DateOnly(2026, 4, 30), null, preSeam: read.Decision.Admitted);
            input.PresetSkip.ShouldBeNull();
            input.PreSeam.ShouldNotBeNull().SeamDate.ShouldBe(new DateOnly(2024, 4, 2));
            input.RawBars.Count.ShouldBe(2);
        }

        // an instrument that is not admitted keeps the guard: the action ex 2024-08-01 falls into the CSV span, which the first bar (2024-04-02) precedes
        var cut = InstrumentInputReader.Build(id, instrument, [Dhan(Seam, 252.10, 10)], factors, false, new(), csvThrough, XDrishti.Domain.Research.ReplayOptions.Default, new DateOnly(2023, 1, 1), new DateOnly(2026, 4, 30), null);
        cut.PresetSkip.ShouldBe(XDrishti.Domain.Research.SkipReason.CsvAdjustedTwice);
    }

    [Fact]
    public async Task A_run_over_instruments_counts_admitted_and_each_first_reason_for_the_label()
    {
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var z = Guid.NewGuid();
        var w = Guid.NewGuid();
        var noRow = Guid.NewGuid();
        var factors = X();
        var seams = new FakeSeams();
        seams.Facts[x] = Facts(Record(x, factors));
        seams.Facts[y] = Facts(Record(y, factors, probe: ProbeStatus.None));
        seams.Facts[z] = Facts(Record(z, factors, jump: true));
        seams.Facts[w] = Facts(Record(w, factors, probe: ProbeStatus.Unresolved));
        seams.Facts[noRow] = new SeamAdmissionFacts(true, null, null, null, false);
        var tally = new AdmissionTally();

        foreach (var id in new[] { x, y, z, w, noRow })
        {
            tally.Count(await DecideAsync(seams, id, factors));
        }

        var record = tally.Record().ShouldNotBeNull();
        record.Admitted.ShouldBe(1);
        record.Label().ShouldBe("backfill: pre-seam bars admitted for 1 instruments (source-aware); excluded for 3 (not_probed 1, seam_jump 1, seam_unresolved 1)");
        new AdmissionTally().Record().ShouldBeNull();
    }
}
