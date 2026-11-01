using System.Reflection;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.OptionQuality;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.Tests.OptionQuality;

public sealed class GetOptionCoverageHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Today = new(2026, 10, 14);

    private static readonly string[] Expected = ["Checks", "Code", "Date", "FetchKey", "Reason", "Sessions", "Source", "Status", "Threshold", "Truncated", "Underlying", "Usable", "UsableChainSessions", "Value"];

    private static QualityCheck Check(string code, QualityStatus status, double? value = null) =>
        new(code, status, value, 0.95, status == QualityStatus.Unavailable ? "not_recorded" : null, 10, null);

    private static async Task AddChainDayAsync(QualityWorld world, DateOnly day, QualityStatus coverage, QualityStatus parity, long run = 1)
    {
        await world.Store.AddResultsAsync(run, [
            new OptionQualityRow(day, QualityWorld.Nifty, "chain", null, null, Check("coverage", coverage, 0.97)),
            new OptionQualityRow(day, QualityWorld.Nifty, "chain", null, null, Check("parity", parity, 0.9)),
            new OptionQualityRow(day, QualityWorld.Nifty, "chain", null, null, Check("greeks", QualityStatus.Fail, 0.5)),
        ], Ct);
    }

    [Fact]
    public async Task Nothing_recorded_is_an_empty_list_and_not_an_error()
    {
        var world = new QualityWorld();

        var result = await world.Handler().HandleAsync(new GetOptionCoverageQuery("NSE:NIFTY", null, null), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Sessions.ShouldBeEmpty();
        result.Value.UsableChainSessions.ShouldBe(0);
        result.Value.Truncated.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("NSE:NOPE")]
    public async Task A_missing_or_unknown_underlying_is_options_invalid_query(string? underlying)
    {
        var result = await new QualityWorld().Handler().HandleAsync(new GetOptionCoverageQuery(underlying, null, null), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("options.invalid_query");
    }

    [Fact]
    public async Task A_range_over_400_days_or_an_inverted_range_is_options_invalid_query_and_exactly_400_days_is_fine()
    {
        var handler = new QualityWorld().Handler();

        var over = await handler.HandleAsync(new GetOptionCoverageQuery("NSE:NIFTY", Today.AddDays(-400), Today), Ct); // 401 days
        var exact = await handler.HandleAsync(new GetOptionCoverageQuery("NSE:NIFTY", Today.AddDays(-399), Today), Ct); // 400 days
        var inverted = await handler.HandleAsync(new GetOptionCoverageQuery("NSE:NIFTY", Today, Today.AddDays(-1)), Ct);

        over.Error.Code.ShouldBe("options.invalid_query");
        exact.IsSuccess.ShouldBeTrue();
        inverted.Error.Code.ShouldBe("options.invalid_query");
    }

    [Fact]
    public async Task A_bare_ticker_in_any_case_means_the_nse_symbol()
    {
        var result = await new QualityWorld().Handler().HandleAsync(new GetOptionCoverageQuery(" nifty ", null, null), Ct);

        result.Value.Underlying.ShouldBe("NSE:NIFTY");
    }

    [Fact]
    public async Task A_session_is_usable_only_when_coverage_passes_and_parity_passes_or_warns_and_the_cumulative_count_counts_those()
    {
        var world = new QualityWorld(new DateTimeOffset(2026, 10, 14, 17, 0, 0, TimeSpan.FromMinutes(330)));
        await AddChainDayAsync(world, new DateOnly(2026, 10, 9), QualityStatus.Pass, QualityStatus.Pass);
        await AddChainDayAsync(world, new DateOnly(2026, 10, 12), QualityStatus.Pass, QualityStatus.Unavailable);
        await AddChainDayAsync(world, new DateOnly(2026, 10, 13), QualityStatus.Warn, QualityStatus.Pass);
        await AddChainDayAsync(world, new DateOnly(2026, 10, 14), QualityStatus.Pass, QualityStatus.Warn);

        var result = await world.Handler().HandleAsync(new GetOptionCoverageQuery("NSE:NIFTY", new DateOnly(2026, 10, 12), Today), Ct);

        result.Value.Sessions.Select(s => (s.Date.Day, s.Usable)).ShouldBe([(12, false), (13, false), (14, true)]);
        result.Value.UsableChainSessions.ShouldBe(2); // the 9th (before the range) and the 14th
    }

    [Fact]
    public async Task The_newest_run_wins_for_a_session_and_an_unavailable_check_is_shown_with_its_reason()
    {
        var world = new QualityWorld();
        await AddChainDayAsync(world, Today, QualityStatus.Fail, QualityStatus.Pass, run: 1);
        await AddChainDayAsync(world, Today, QualityStatus.Unavailable, QualityStatus.Pass, run: 2);

        var result = await world.Handler().HandleAsync(new GetOptionCoverageQuery("NSE:NIFTY", Today, Today), Ct);

        var coverage = result.Value.Sessions.Single().Checks.Single(c => c.Code == "coverage");
        coverage.Status.ShouldBe("unavailable");
        coverage.Reason.ShouldBe("not_recorded");
        result.Value.Sessions.Single().Usable.ShouldBeFalse();
    }

    [Fact]
    public async Task Rolling_sessions_are_listed_per_fetch_key()
    {
        var world = new QualityWorld();
        foreach (var key in new[] { "k-a", "k-b" })
        {
            await world.Store.AddResultsAsync(1, [
                new OptionQualityRow(Today, QualityWorld.Nifty, "rolling", null, key, Check("coverage", QualityStatus.Pass, 1)),
                new OptionQualityRow(Today, QualityWorld.Nifty, "rolling", null, key, Check("stitching", QualityStatus.Pass, 0)),
            ], Ct);
        }

        var result = await world.Handler().HandleAsync(new GetOptionCoverageQuery("NSE:NIFTY", Today, Today), Ct);

        result.Value.Sessions.Select(s => s.FetchKey).ShouldBe(["k-a", "k-b"]);
        result.Value.Sessions.ShouldAllBe(s => s.Source == "rolling" && s.Usable);
        result.Value.UsableChainSessions.ShouldBe(0);
    }

    [Fact]
    public async Task The_rolling_list_is_cut_by_key_sessions_never_in_the_middle_of_one_and_says_it_was_cut()
    {
        var world = new QualityWorld();
        var rows = new List<OptionQualityRow>();
        for (var i = 0; i < 5001; i++)
        {
            var key = $"k{i:D5}";
            rows.Add(new OptionQualityRow(Today, QualityWorld.Nifty, "rolling", null, key, Check("coverage", QualityStatus.Pass, 1)));
            rows.Add(new OptionQualityRow(Today, QualityWorld.Nifty, "rolling", null, key, Check("stitching", QualityStatus.Pass, 0)));
        }

        await world.Store.AddResultsAsync(1, rows, Ct);

        var result = await world.Handler().HandleAsync(new GetOptionCoverageQuery("NSE:NIFTY", Today, Today), Ct);

        result.Value.Truncated.ShouldBeTrue();
        result.Value.Sessions.Count.ShouldBe(5000);
        result.Value.Sessions.ShouldAllBe(s => s.Checks.Count == 2 && s.Usable);
    }

    [Fact]
    public void The_views_carry_no_price_iv_or_greek()
    {
        var names = new[] { typeof(OptionCoverageView), typeof(OptionCoverageSessionView), typeof(OptionCoverageCheckView) }
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)).Select(p => p.Name).Order(StringComparer.Ordinal).ToList();

        names.ShouldBe(Expected);
    }
}
