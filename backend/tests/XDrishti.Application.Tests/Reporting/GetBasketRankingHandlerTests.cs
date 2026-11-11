using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>BK-07 ranking behind the ports: the rank on relative strength, no rank without a value, "not separated", the overlap and the refusals.</summary>
public sealed class GetBasketRankingHandlerTests
{
    private static readonly string From = ProfileWorld.Sessions[0].ToString("yyyy-MM-dd");
    private static readonly string To = ProfileWorld.Sessions[^1].ToString("yyyy-MM-dd");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ProfileWorld _world = new();

    private (Guid Strong, Guid Weak, Guid Flat, Guid Empty) Baskets()
    {
        _world.IndexIsBenchmark(_world.Instrument("NIFTY", i => 1000 + (i * 100.0 / 24)));
        var a = _world.Instrument("A", i => 100 + (3 * i));
        var b = _world.Instrument("B", i => 100 + (3 * i));
        var c = _world.Instrument("C", i => 100 + (3 * i));
        var d = _world.Instrument("D", i => 100 - i);
        var e = _world.Instrument("E", i => 100 - i);
        return (_world.Basket("Strong", null, a, b, c), _world.Basket("Weak", null, d, e, _world.Instrument("F", i => 100 - i)), _world.Basket("Mixed", null, a, b, d), _world.Basket("Empty", null, _world.Instrument("X"), _world.Instrument("Y"), _world.Instrument("Z")));
    }

    private async Task<BasketRankingView> OkAsync(params Guid[] ids)
    {
        var result = await _world.Ranking().HandleAsync(new GetBasketRankingQuery(string.Join(',', ids.Select(i => i.ToString("D"))), From, To, null), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code + ": " + result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public async Task The_stronger_basket_ranks_first_on_the_relative_strength_window_and_no_basket_is_labelled()
    {
        var (strong, weak, _, _) = Baskets();

        var view = await OkAsync(weak, strong);

        view.RankWindow.ShouldBe(21);
        view.Rows.Select(r => r.Name).ShouldBe(["Weak", "Strong"]);
        view.Rows.Select(r => r.RsRank.Number).ShouldBe([2, 1]);
        view.Rows[1].RsReturn21.State.ShouldBe("value");
        view.Rows[1].RsReturn21.Number!.Value.ShouldBeGreaterThan(view.Rows[0].RsReturn21.Number!.Value);
        view.Rows[1].RsReturn63.State.ShouldBe("insufficient");
        view.Rows[1].RsReturn63.Number.ShouldBeNull();
        view.Rows[1].RiskAdjusted.State.ShouldBe("value");
        view.Separated.ShouldBeTrue();
    }

    [Fact]
    public async Task A_basket_with_no_bars_shows_its_state_and_no_rank()
    {
        var (strong, weak, _, empty) = Baskets();

        var view = await OkAsync(strong, empty, weak);

        var row = view.Rows[1];
        row.State.ShouldNotBe("value");
        row.RsRank.Number.ShouldBeNull();
        row.RsReturn21.Number.ShouldBeNull();
        row.RiskAdjusted.Number.ShouldBeNull();
        view.Rows[0].RsRank.Number.ShouldBe(1);
        view.Rows[2].RsRank.Number.ShouldBe(2);
    }

    [Fact]
    public async Task Baskets_that_share_members_give_the_jaccard_overlap_and_baskets_with_identical_returns_are_not_separated()
    {
        var (strong, _, mixed, _) = Baskets();
        var same = _world.Basket("Same", null, _world.Instrument("A2", i => 100 + (3 * i)), _world.Instrument("B2", i => 100 + (3 * i)), _world.Instrument("C2", i => 100 + (3 * i)));

        var overlap = await OkAsync(strong, mixed);
        var twins = await OkAsync(strong, same);

        // {A,B,C} against {A,B,D}: two shared of four
        overlap.Overlap.Single().Jaccard.Number!.Value.ShouldBe(0.5, 1e-12);
        overlap.Overlap.Single().A.ShouldBe(strong);
        twins.Overlap.Single().Jaccard.Number!.Value.ShouldBe(0.0, 1e-12);
        twins.Separated.ShouldBeFalse();
    }

    [Fact]
    public async Task Without_benchmark_bars_the_strengths_are_unavailable_and_nothing_is_ranked()
    {
        var a = _world.Instrument("A", i => 100 + i);
        var b = _world.Instrument("B", i => 100 + i);
        var c = _world.Instrument("C", i => 100 + i);
        var one = _world.Basket("One", null, a, b, c);
        var two = _world.Basket("Two", null, a, b, c);

        var view = await OkAsync(one, two);

        view.Rows.ShouldAllBe(r => r.RsReturn21.State == "unavailable" && r.RsRank.Number == null);
        view.RankWindow.ShouldBeNull();
        view.Evidence.UnavailableInputs.ShouldContain(s => s.StartsWith("benchmark index", StringComparison.Ordinal));
    }

    [Fact]
    public async Task One_bar_read_per_instrument_however_many_baskets_share_it_and_the_evidence_block_is_present()
    {
        var (strong, weak, mixed, _) = Baskets();

        var view = await OkAsync(strong, weak, mixed);

        // A, B, C, D, E, F and the index
        _world.DailyReads.Count.ShouldBe(7);
        view.Evidence.Basis.ShouldBe("equal weight, daily rebalanced, price return, dividends not included");
        view.Evidence.Limits.ShouldNotBeEmpty();
        view.Evidence.N.ShouldBe(6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(13)]
    public async Task Fewer_than_two_or_more_than_twelve_baskets_is_422(int count)
    {
        var (strong, _, _, _) = Baskets();
        var ids = Enumerable.Range(0, count).Select(i => i == 0 ? strong : Guid.NewGuid());

        var result = await _world.Ranking().HandleAsync(new GetBasketRankingQuery(string.Join(',', ids), From, To, null), Ct);

        result.Error.Code.ShouldBe("reports.invalid");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task An_unknown_basket_is_404_and_a_malformed_id_is_422()
    {
        var (strong, _, _, _) = Baskets();

        var missing = await _world.Ranking().HandleAsync(new GetBasketRankingQuery($"{strong:D},{Guid.NewGuid():D}", From, To, null), Ct);
        var malformed = await _world.Ranking().HandleAsync(new GetBasketRankingQuery($"{strong:D},nope", From, To, null), Ct);

        missing.Error.Code.ShouldBe("reports.not_found");
        malformed.Error.Code.ShouldBe("reports.invalid");
    }
}
