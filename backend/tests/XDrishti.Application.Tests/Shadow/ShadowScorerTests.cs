using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Shadow;
using XDrishti.Application.Tests.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Shadow;

namespace XDrishti.Application.Tests.Shadow;

/// <summary>CF-10e part 10e-1 (ADR 0281): every registered model scores every candidate at the close before its session, with no look-ahead, no imputation and at most five models.</summary>
public sealed class ShadowScorerTests
{
    private static readonly DateOnly Session = new(2026, 11, 3);

    private static readonly Guid Instrument = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static readonly long Decision = new DateTimeOffset(new DateTime(2026, 11, 2), IndiaTime.Offset).ToUnixTimeSeconds() + (SessionClock.CloseMinute * 60L);

    private readonly IModelRegistry _registry = Substitute.For<IModelRegistry>();

    private readonly IModelFiles _files = Substitute.For<IModelFiles>();

    private readonly IModelScorerLoader _loader = Substitute.For<IModelScorerLoader>();

    private readonly IBasketQueries _baskets = Substitute.For<IBasketQueries>();

    private readonly IBarStore _bars = Substitute.For<IBarStore>();

    private readonly IBackfillSeams _seams = Substitute.For<IBackfillSeams>();

    private readonly IAdjustmentFactors _adjustments = Substitute.For<IAdjustmentFactors>();

    private readonly ICellStatsStore _cells = Substitute.For<ICellStatsStore>();

    private readonly List<(long From, long To)> _reads = [];

    private List<Bar> _stored = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StoredModel Wide(long id, ModelRole role, char hash) =>
        MlInfluenceKit.Model(id, role, hash: hash) with { Model = MlInfluenceKit.Model(id, role, hash: hash).Model with { FeatureWidth = TradeFeatureCatalog.All.Count } };

    private static ShadowEntry Entry(string symbol, string key, string cell = ShadowFixture.Intraday, decimal? stop = 98m) =>
        new(Session, 10, 100, key, "primary", cell, ShadowFixture.Hash, symbol, "BUY", 100m, stop, 104m, "09:30-11:30", null, null, null, null, null, true, 3);

    private ShadowScorer Scorer(ITradeFeatureExtractor extractor)
    {
        _bars.ReadAsync(Instrument, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct).Returns(call =>
        {
            var from = call.ArgAt<DateTimeOffset>(1).ToUnixTimeSeconds();
            var to = call.ArgAt<DateTimeOffset>(2).ToUnixTimeSeconds();
            _reads.Add((from, to));
            return (IReadOnlyList<Bar>)[.. _stored.Where(b => b.EpochSeconds >= from && b.EpochSeconds <= to)];
        });
        _seams.SeamOfAsync(Instrument, Ct).Returns((DateTimeOffset?)null);
        _adjustments.GetAsync(Instrument, Ct).Returns(AdjustmentFactors.None);
        var member = new BasketMemberView(Guid.NewGuid(), new InstrumentSummary(Instrument, "NSE:P1", "n", "n", "EQ", null, null, null, 1, null, null, false, false, false, null!), 1, DateTimeOffset.UnixEpoch, null, null, "t", null, null);
        _baskets.ListAsync(false, Ct).Returns((IReadOnlyList<BasketView>)[new BasketView(Guid.NewGuid(), "b", "c", "p", null, 1, 1, DateTimeOffset.UnixEpoch, null, "manual", false, null, null)]);
        _baskets.MembersAsync(Arg.Any<Guid>(), null, null, Ct).Returns(new BasketMembers(Guid.NewGuid(), "b", null, null, 1, [member]));
        var row = new CellStatsRow(CellId.TryParse(ShadowFixture.Intraday)!, CellWindow.All, new DateOnly(2026, 10, 30), true, 40, 20, 40, 0.5, 0.5, 0.55, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        _cells.ListLatestBeforeAsync(Session, null, "all", Ct).Returns((IReadOnlyList<StoredCellStats>)[new StoredCellStats("k", DateTimeOffset.UnixEpoch, new CellStatsOptions(), "", row)]);
        _files.ReadAsync(Arg.Any<string>(), Ct).Returns(call => Result.Success(new byte[] { 1 }));
        return new ShadowScorer(_registry, _files, _loader, _baskets, _bars, _seams, _adjustments, extractor, _cells, NullLogger<ShadowScorer>.Instance);
    }

    private void Models(params StoredModel[] models)
    {
        _registry.ListAsync(ModelRole.Champion, Arg.Any<int>(), Ct).Returns((IReadOnlyList<StoredModel>)[.. models.Where(m => m.Role == ModelRole.Champion)]);
        _registry.ListAsync(ModelRole.Challenger, Arg.Any<int>(), Ct).Returns((IReadOnlyList<StoredModel>)[.. models.Where(m => m.Role == ModelRole.Challenger)]);
        _loader.Load(Arg.Any<byte[]>(), Arg.Any<string>()).Returns(call => Result.Success<IExplainableScorer>(new LastCloseScorer()));
    }

    /// <summary>An extractor that obeys its contract and reads the last close of the bars it is given into every stored value.</summary>
    private sealed class CloseExtractor : ITradeFeatureExtractor
    {
        public IReadOnlyDictionary<string, double[]> Extract(IReadOnlyList<Bar> rawBars, AdjustmentFactors factors, TradingCalendar calendar, IReadOnlyList<TradeFeatureDecision> decisions) =>
            decisions.ToDictionary(d => d.TradeKey, d => Enumerable.Repeat(rawBars.Where(b => b.EpochSeconds < d.DecisionEpochSeconds).Last().Close, TradeFeatureCatalog.StoredCount).ToArray());
    }

    private sealed class LastCloseScorer : IExplainableScorer
    {
        public double Score(ValidationRow row) => Math.Clamp(row.Features[0] / 1000, 0, 1);

        public IReadOnlyList<FeatureContribution> Explain(ValidationRow row, int top) => [];
    }

    private void Bars(double lastBeforeClose, double afterClose)
    {
        _stored = [new Bar(Decision - 120, lastBeforeClose, lastBeforeClose, lastBeforeClose, lastBeforeClose, 1), new Bar(Decision - 60, lastBeforeClose, lastBeforeClose, lastBeforeClose, lastBeforeClose, 1), new Bar(Decision + 60, afterClose, afterClose, afterClose, afterClose, 1)];
    }

    [Fact]
    public async Task The_champion_and_the_challenger_both_score_every_candidate_with_a_probability_and_a_hash()
    {
        Models(Wide(17, ModelRole.Champion, 'a'), Wide(18, ModelRole.Challenger, 'b'));
        Bars(400, 900);

        var scores = await Scorer(new CloseExtractor()).ScoreAsync(Session, new TradingCalendar(), [Entry("NSE:P1", "k1"), Entry("NSE:P1", "k2")], Ct);

        scores.Count.ShouldBe(4);
        scores.Select(s => s.ModelId).Distinct().Order().ShouldBe([17L, 18L]);
        scores.ShouldAllBe(s => s.PWin >= 0 && s.PWin <= 1 && s.FeaturesHash.Length == 64 && s.FeatureVersion == "tf-1");
        scores.First(s => s.ModelId == 17).ModelSha256.ShouldBe(new string('a', 64));
    }

    [Fact]
    public async Task No_bar_after_the_decision_close_is_read_and_changing_them_changes_no_score()
    {
        Models(Wide(17, ModelRole.Champion, 'a'));
        Bars(400, 900);
        var scorer = Scorer(new CloseExtractor());
        var first = await scorer.ScoreAsync(Session, new TradingCalendar(), [Entry("NSE:P1", "k1")], Ct);

        Bars(400, 1);
        var second = await scorer.ScoreAsync(Session, new TradingCalendar(), [Entry("NSE:P1", "k1")], Ct);

        second.ShouldBe(first);
        first.Single().PWin.ShouldBe(0.4);
        _reads.ShouldAllBe(r => r.To < Decision);
    }

    [Fact]
    public async Task A_candidate_whose_cell_has_no_statistics_or_whose_stop_is_missing_gets_no_score_and_nothing_is_imputed()
    {
        Models(Wide(17, ModelRole.Champion, 'a'));
        Bars(400, 900);

        var scores = await Scorer(new CloseExtractor()).ScoreAsync(Session, new TradingCalendar(), [Entry("NSE:P1", "k1", ShadowFixture.Daily), Entry("NSE:P1", "k2", stop: null), Entry("NSE:UNKNOWN", "k3")], Ct);

        scores.ShouldBeEmpty();
    }

    [Fact]
    public async Task At_most_five_models_score_and_a_model_of_another_feature_width_is_not_scored()
    {
        var models = new List<StoredModel> { Wide(1, ModelRole.Champion, 'a'), MlInfluenceKit.Model(99, ModelRole.Challenger, hash: 'f') };
        models.AddRange(Enumerable.Range(2, 6).Select(i => Wide(i, ModelRole.Challenger, (char)('a' + i))));
        Models([.. models]);
        Bars(400, 900);

        var scores = await Scorer(new CloseExtractor()).ScoreAsync(Session, new TradingCalendar(), [Entry("NSE:P1", "k1")], Ct);

        scores.Select(s => s.ModelId).Order().ShouldBe([1L, 2L, 3L, 4L, 5L]);
    }
}
