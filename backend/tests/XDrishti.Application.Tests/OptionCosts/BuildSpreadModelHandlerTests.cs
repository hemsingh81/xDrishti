using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.OptionCosts;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.Tests.OptionCosts;

/// <summary>P14-07 criteria 3 and 8 (use case): which sessions are read, when no model is built, and that a rebuild with the same inputs reads and writes nothing.</summary>
public sealed class BuildSpreadModelHandlerTests
{
    private static readonly Guid Nifty = Guid.Parse("00000000-0000-7000-8000-000000000013");
    private static readonly DateOnly First = new(2026, 1, 5);
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    private readonly IOptionReference _reference = Substitute.For<IOptionReference>();
    private readonly IOptionArchiveReader _archive = Substitute.For<IOptionArchiveReader>();
    private readonly IOptionQualityStore _quality = Substitute.For<IOptionQualityStore>();
    private readonly RecordingReader _quotes = new();
    private readonly ISpreadModelStore _store = Substitute.For<ISpreadModelStore>();
    private readonly ISpreadModelSettings _settings = Substitute.For<ISpreadModelSettings>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, India));

    public BuildSpreadModelHandlerTests()
    {
        _settings.MinUsableSessions.Returns(60);
        _settings.MinQuotesPerBucket.Returns(2);
        _reference.FindUnderlyingAsync("NSE:NIFTY", Arg.Any<CancellationToken>()).Returns(Nifty);
        _store.AppendAsync(Arg.Any<BuiltSpreadModel>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private BuildSpreadModelHandler Handler() => new(_reference, _archive, _quality, _quotes, _store, _settings, _time);

    private static OptionQualityRow Row(DateOnly day, string code, QualityStatus status) =>
        new(day, Nifty, QualityCodes.Chain, null, null, new QualityCheck(code, status, null, null, null, 1, null));

    /// <summary>Sessions from the first day on: usable ones (coverage pass, parity pass or warn), and unusable ones of each kind.</summary>
    private void Archive(int usable, int parityFail = 0, int coverageWarn = 0, int parityUnavailable = 0, int coverageOnly = 0)
    {
        var rows = new List<OptionQualityRow>();
        var day = First;
        for (var i = 0; i < usable; i++, day = day.AddDays(1))
        {
            rows.Add(Row(day, QualityCodes.Coverage, QualityStatus.Pass));
            rows.Add(Row(day, QualityCodes.Parity, i % 2 == 0 ? QualityStatus.Pass : QualityStatus.Warn));
        }

        for (var i = 0; i < parityFail; i++, day = day.AddDays(1))
        {
            rows.Add(Row(day, QualityCodes.Coverage, QualityStatus.Pass));
            rows.Add(Row(day, QualityCodes.Parity, QualityStatus.Fail));
        }

        for (var i = 0; i < coverageWarn; i++, day = day.AddDays(1))
        {
            rows.Add(Row(day, QualityCodes.Coverage, QualityStatus.Warn));
            rows.Add(Row(day, QualityCodes.Parity, QualityStatus.Pass));
        }

        for (var i = 0; i < parityUnavailable; i++, day = day.AddDays(1))
        {
            rows.Add(Row(day, QualityCodes.Coverage, QualityStatus.Pass));
            rows.Add(Row(day, QualityCodes.Parity, QualityStatus.Unavailable));
        }

        for (var i = 0; i < coverageOnly; i++, day = day.AddDays(1))
        {
            rows.Add(Row(day, QualityCodes.Coverage, QualityStatus.Pass));
        }

        _quality.ChainResultsAsync(Nifty, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(rows);
    }

    [Fact]
    public async Task Sixty_usable_sessions_build_a_model_and_only_those_sessions_are_read()
    {
        Archive(usable: 60, parityFail: 3, coverageWarn: 2, parityUnavailable: 2, coverageOnly: 1);
        var usable = Enumerable.Range(0, 60).Select(i => First.AddDays(i)).ToList();

        var result = await Handler().HandleAsync(new BuildSpreadModelCommand("NSE:NIFTY"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var line = result.Value.Lines.ShouldHaveSingleItem();
        line.Status.ShouldBe("built");
        line.UsableSessions.ShouldBe(60);
        _quotes.Asked.ShouldBe(usable);
        line.ModelId.ShouldBe(SpreadModelId.Of(Nifty, usable, 60, 2));
        await _store.Received(1).AppendAsync(
            Arg.Is<BuiltSpreadModel>(m => m.ModelId == line.ModelId && m.Sessions == 60 && m.FirstSession == First && m.LastSession == First.AddDays(59) && m.UnderlyingId == Nifty && m.SettingsJson == SpreadModelId.SettingsJson(60, 2)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fifty_nine_usable_sessions_build_no_model_say_sessions_59_of_60_and_read_nothing()
    {
        Archive(usable: 59, parityFail: 5, coverageWarn: 5);

        var result = await Handler().HandleAsync(new BuildSpreadModelCommand("NSE:NIFTY"), TestContext.Current.CancellationToken);

        var line = result.Value.Lines.ShouldHaveSingleItem();
        line.Status.ShouldBe("not_built");
        line.Reason.ShouldBe("sessions 59 of 60");
        line.ModelId.ShouldBeNull();
        _quotes.Asked.ShouldBeEmpty();
        await _store.DidNotReceiveWithAnyArgs().AppendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_rebuild_with_the_same_inputs_reads_no_quote_and_writes_nothing()
    {
        Archive(usable: 60);
        _store.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler().HandleAsync(new BuildSpreadModelCommand("NSE:NIFTY"), TestContext.Current.CancellationToken);

        result.Value.Lines.ShouldHaveSingleItem().Status.ShouldBe("already_built");
        _quotes.Asked.ShouldBeEmpty();
        await _store.DidNotReceiveWithAnyArgs().AppendAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_model_stored_meanwhile_by_another_run_is_reported_as_already_built()
    {
        Archive(usable: 60);
        _store.AppendAsync(Arg.Any<BuiltSpreadModel>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Handler().HandleAsync(new BuildSpreadModelCommand("NSE:NIFTY"), TestContext.Current.CancellationToken);

        result.Value.Lines.ShouldHaveSingleItem().Status.ShouldBe("already_built");
    }

    [Fact]
    public async Task The_buckets_of_the_stored_model_are_the_medians_of_the_quotes_read()
    {
        Archive(usable: 60);
        _settings.MinQuotesPerBucket.Returns(60);
        BuiltSpreadModel? stored = null;
        _store.AppendAsync(Arg.Do<BuiltSpreadModel>(m => stored = m), Arg.Any<CancellationToken>()).Returns(true);
        foreach (var day in Enumerable.Range(0, 60).Select(i => First.AddDays(i)))
        {
            var ts = new DateTimeOffset(day.Year, day.Month, day.Day, 11, 0, 0, India);
            _quotes.Sessions[day] =
            [
                new SpreadQuote(ts, day.AddDays(1), 2_200_000, 2_200_100, 9_900, 10_100), // 1.0 %
                new SpreadQuote(ts, day.AddDays(1), 2_205_000, 2_200_100, null, 10_100),
            ];
        }

        await Handler().HandleAsync(new BuildSpreadModelCommand("NSE:NIFTY"), TestContext.Current.CancellationToken);

        stored.ShouldNotBeNull().Buckets.ShouldContain(new SpreadBucketRow("0", "0-2", "10:00-14:30", 60, 1.0));
    }

    [Fact]
    public async Task Without_a_symbol_every_recorded_underlying_is_built_in_symbol_order_and_none_recorded_is_a_reason_not_a_failure()
    {
        var bank = Guid.Parse("00000000-0000-7000-8000-000000000025");
        _archive.RecentChainUnderlyingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([Nifty, bank]);
        _reference.SymbolsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, string> { [Nifty] = "NSE:NIFTY", [bank] = "NSE:BANKNIFTY" });
        Archive(usable: 10);
        _quality.ChainResultsAsync(bank, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);

        var all = await Handler().HandleAsync(new BuildSpreadModelCommand(null), TestContext.Current.CancellationToken);

        all.Value.Lines.Select(l => l.Symbol).ShouldBe(["NSE:BANKNIFTY", "NSE:NIFTY"]);
        all.Value.Lines.Select(l => l.Reason).ShouldBe(["sessions 0 of 60", "sessions 10 of 60"]);

        _archive.RecentChainUnderlyingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        var none = await Handler().HandleAsync(new BuildSpreadModelCommand(null), TestContext.Current.CancellationToken);

        none.IsSuccess.ShouldBeTrue();
        none.Value.Lines.ShouldBeEmpty();
        none.Value.Reason.ShouldBe("no underlying has chain sessions recorded");
    }

    [Theory]
    [InlineData("NIFTY")]
    [InlineData("nifty")]
    [InlineData(" nse:nifty ")]
    public async Task A_symbol_typed_without_the_exchange_or_in_lower_case_is_normalised_as_the_coverage_route_does(string typed)
    {
        Archive(usable: 10);

        var result = await Handler().HandleAsync(new BuildSpreadModelCommand(typed), TestContext.Current.CancellationToken);

        result.Value.Lines.ShouldHaveSingleItem().Symbol.ShouldBe("NSE:NIFTY");
    }

    [Fact]
    public async Task An_unknown_symbol_is_a_failure_with_its_own_code()
    {
        var result = await Handler().HandleAsync(new BuildSpreadModelCommand("NSE:NOPE"), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(BuildSpreadModelHandler.UnknownUnderlyingCode);
    }

    /// <summary>Records each session asked for; answers the quotes a test set, else none.</summary>
    private sealed class RecordingReader : IChainSpreadReader
    {
        public List<DateOnly> Asked { get; } = [];

        public Dictionary<DateOnly, SpreadQuote[]> Sessions { get; } = [];

        public ValueTask<IReadOnlyList<SpreadQuote>> QuotesAsync(Guid underlyingId, DateOnly session, CancellationToken cancellationToken)
        {
            Asked.Add(session);
            return ValueTask.FromResult<IReadOnlyList<SpreadQuote>>(Sessions.TryGetValue(session, out var quotes) ? quotes : []);
        }
    }
}
