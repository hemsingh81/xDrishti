using NSubstitute;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Tests.Filings;

/// <summary>Spec P15-02 criterion 11 on fakes: the validation of the two routes, the unavailable state of a kind without data and the asOf parse.</summary>
public sealed class FilingReadHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Instrument = new("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly FilingView View = new(Guid.NewGuid(), "NSE:RELIANCE", "results", new DateOnly(2026, 6, 30), FilingWorld.Broadcast, "ok", null, FilingWorld.Broadcast, new string('a', 64), 10, "r.xml", null);

    private sealed class Fixture
    {
        public IFilingReader Reader { get; } = Substitute.For<IFilingReader>();

        public IFilingInstruments Instruments { get; } = Substitute.For<IFilingInstruments>();

        public IPointInTimeFilings PointInTime { get; } = Substitute.For<IPointInTimeFilings>();

        public Fixture()
        {
            Instruments.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(InstrumentMatch.Unknown);
            Reader.ByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
            Instruments.ResolveAsync("RELIANCE", Arg.Any<CancellationToken>()).Returns(InstrumentMatch.One(Instrument));
            Reader.ListAsync(Arg.Any<Guid?>(), Arg.Any<FilingKind?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        }

        public ListFilingsHandler List() => new(Reader, Instruments, PointInTime);
    }

    [Theory]
    [InlineData("nope", "RELIANCE", null)]
    [InlineData("results", "NOSUCH", null)]
    [InlineData("results", "RELIANCE", "2026-07-24T16:17:00")]
    [InlineData("results", "RELIANCE", "2026-07-24")]
    [InlineData("results", "RELIANCE", "garbage")]
    public async Task A_bad_kind_instrument_or_asOf_is_filings_invalid_query(string kind, string instrument, string? asOf)
    {
        var result = await new Fixture().List().HandleAsync(new ListFilingsQuery(instrument, kind, asOf), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("filings.invalid_query");
    }

    [Fact]
    public async Task An_asOf_without_an_instrument_and_a_kind_is_refused()
    {
        var result = await new Fixture().List().HandleAsync(new ListFilingsQuery(null, "results", "2026-07-24T10:47:00Z"), Ct);

        result.Error.Code.ShouldBe("filings.invalid_query");
    }

    [Theory]
    [InlineData("2026-07-24T16:17:00+05:30")]
    [InlineData("2026-07-24T10:47:00Z")]
    [InlineData("2026-07-24T10:47:00.5Z")]
    public async Task An_asOf_with_an_offset_asks_the_point_in_time_port_with_that_instant(string asOf)
    {
        var fixture = new Fixture();
        fixture.PointInTime.AsOf(Instrument, FilingKind.Results, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new AsOfResult(AsOfResult.NoneVisible, [], 0));

        var result = await fixture.List().HandleAsync(new ListFilingsQuery("RELIANCE", "results", asOf), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe(AsOfResult.NoneVisible);
        result.Value.Items.ShouldBeEmpty();
        await fixture.PointInTime.Received(1).AsOf(Instrument, FilingKind.Results, DateTimeOffset.Parse(asOf, System.Globalization.CultureInfo.InvariantCulture), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_asOf_list_carries_the_state_the_unavailable_count_and_the_metadata_of_the_current_filings()
    {
        var fixture = new Fixture();
        var filing = new StoredFiling(Guid.NewGuid(), Instrument, FilingKind.Results, new DateOnly(2026, 6, 30), FilingWorld.Broadcast, BroadcastState.Ok, null, FilingWorld.Broadcast, new string('a', 64), 10, "r.xml", null);
        fixture.PointInTime.AsOf(Instrument, FilingKind.Results, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new AsOfResult(AsOfResult.Value, [filing], 2));
        fixture.Reader.ByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([View with { Id = filing.Id }]);

        var result = await fixture.List().HandleAsync(new ListFilingsQuery("RELIANCE", "results", "2026-08-01T00:00:00Z"), Ct);

        (result.Value.State, result.Value.UnavailableCount, result.Value.Items.Single().Id).ShouldBe((AsOfResult.Value, 2, filing.Id));
    }

    [Fact]
    public async Task Without_asOf_nothing_stored_is_an_empty_list_and_a_long_list_is_cut_at_200_with_a_flag()
    {
        var fixture = new Fixture();
        (await fixture.List().HandleAsync(new ListFilingsQuery(null, null, null), Ct)).Value.Items.ShouldBeEmpty();
        fixture.Reader.ListAsync(null, null, 201, Arg.Any<CancellationToken>()).Returns(Enumerable.Repeat(View, 201).ToList());

        var result = await fixture.List().HandleAsync(new ListFilingsQuery(null, null, null), Ct);

        (result.Value.Items.Count, result.Value.Truncated).ShouldBe((200, true));
    }

    [Fact]
    public async Task An_unknown_filing_id_is_not_found()
    {
        var reader = Substitute.For<IFilingReader>();
        reader.FindAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((FilingView?)null);

        var result = await new GetFilingHandler(reader).HandleAsync(new GetFilingQuery(Guid.NewGuid()), Ct);

        result.Error.Code.ShouldBe("filings.not_found");
    }

    [Theory]
    [InlineData("2026-01-10", "2026-01-01")]
    [InlineData("2023-01-01", "2026-01-01")]
    public async Task An_inverted_or_too_long_coverage_range_is_filings_invalid_query(string from, string to)
    {
        var handler = new GetFilingCoverageHandler(Substitute.For<IFilingReader>(), TimeProvider.System);

        var result = await handler.HandleAsync(new GetFilingCoverageQuery(DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture)), Ct);

        result.Error.Code.ShouldBe("filings.invalid_query");
    }

    [Fact]
    public async Task A_kind_with_no_data_answers_unavailable_and_not_zero_and_a_kind_with_data_shows_its_quarters()
    {
        var reader = Substitute.For<IFilingReader>();
        var quarter = new DateOnly(2026, 6, 30);
        reader.CoverageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new FilingCoverageData(
            [new FilingCoverageCell("results", quarter, 3, 1, 0, new Dictionary<string, int> { ["date_only"] = 2 })], 10, new Dictionary<string, int> { ["results"] = 7 }));
        var handler = new GetFilingCoverageHandler(reader, TimeProvider.System);

        var result = await handler.HandleAsync(new GetFilingCoverageQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), Ct);

        var results = result.Value.Kinds.Single(k => k.Kind == "results");
        (results.State, results.InstrumentsWithout, results.Quarters.Single().Ok, results.Quarters.Single().Unavailable.Single().Reason).ShouldBe((AsOfResult.Value, 7, 3, "date_only"));
        var shareholding = result.Value.Kinds.Single(k => k.Kind == "shareholding");
        (shareholding.State, shareholding.Quarters.Count, shareholding.InstrumentsWithout).ShouldBe((AsOfResult.Unavailable, 0, 10));
        result.Value.Kinds.Count.ShouldBe(4);
    }

    [Fact]
    public async Task An_asOf_before_the_year_2000_is_refused()
    {
        var result = await new Fixture().List().HandleAsync(new ListFilingsQuery("RELIANCE", "results", "0001-01-01T00:00:00Z"), Ct);

        result.Error.Code.ShouldBe("filings.invalid_query");
    }

    [Fact]
    public async Task The_asOf_list_is_cut_newest_first_and_only_the_rows_to_show_are_read()
    {
        var fixture = new Fixture();
        var filings = Enumerable.Range(0, 205).Select(i => new StoredFiling(Guid.NewGuid(), Instrument, FilingKind.Announcement, null, FilingWorld.Broadcast.AddMinutes(i), BroadcastState.Ok, null, FilingWorld.Broadcast, new string('a', 64), 1, "a.xml", null)).ToList();
        fixture.PointInTime.AsOf(Instrument, FilingKind.Announcement, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(new AsOfResult(AsOfResult.Value, filings, 0));
        fixture.Reader.ByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<IReadOnlyCollection<Guid>>().Select(id => View with { Id = id }).ToList());

        var result = await fixture.List().HandleAsync(new ListFilingsQuery("RELIANCE", "announcement", "2027-01-01T00:00:00Z"), Ct);

        result.Value.Truncated.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(200);
        result.Value.Items.Select(i => i.Id).ShouldBe(filings.OrderByDescending(f => f.BroadcastTs).Take(200).Select(f => f.Id));
        await fixture.Reader.Received(1).ByIdsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 201), Arg.Any<CancellationToken>());
    }
}
