using System.Runtime.Versioning;
using XDrishti.Api.Simulate;
using XDrishti.Api.Simulate.Assist;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;
using static XDrishti.Api.IntegrationTests.Simulate.AssistSupport;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-09c criterion 4 and the folder path: the Dhan source against a stub of the intraday source (never the live service), the folder source, the cache file and the command line of <c>--assist</c>.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class AssistSecondSourceTests
{
    private static readonly FetchTarget Target = new(Guid.NewGuid(), Symbol, "share", "1333", "NSE_EQ", null);

    private static List<Bar> DhanDay() => [.. BuyDay().Select(b => new Bar(b.EpochSeconds, (double)b.Open, (double)b.High, (double)b.Low, (double)b.Close, (long)b.Volume))];

    private sealed class StubIntraday(Func<IntradayFetchPlan, IntradayResult> answer) : IIntradayBarSource
    {
        public List<IntradayFetchPlan> Plans { get; } = [];

        public ValueTask<SourceAvailability> CheckAsync(CancellationToken cancellationToken) => ValueTask.FromResult(SourceAvailability.Ready);

        public async IAsyncEnumerable<IntradayResult> FetchAsync(IntradayFetchPlan plan, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Plans.Add(plan);
            await Task.CompletedTask;
            yield return answer(plan);
        }
    }

    private sealed class StubTargets : IFetchTargets
    {
        public ValueTask<IReadOnlyList<FetchTarget>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<FetchTarget>>([Target]);
    }

    private static (DhanSecondSource Source, StubIntraday Stub) Dhan(Func<IntradayFetchPlan, IntradayResult> answer)
    {
        var stub = new StubIntraday(answer);
        return (new DhanSecondSource(new StubTargets(), stub), stub);
    }

    [Fact]
    public async Task A_good_answer_gives_the_bars_of_the_day_with_one_window_of_one_day_in_one_call()
    {
        var (source, stub) = Dhan(p => new IntradayResult(Target.InstrumentId, DhanDay(), 1));

        var day = await source.GetAsync(Symbol, Date, TestContext.Current.CancellationToken);

        day.Bars.ShouldNotBeNull();
        day.Bars.Count.ShouldBe(BuyDay().Count);
        stub.Plans.Count.ShouldBe(1);
        stub.Plans[0].Targets.Single().Windows.Count.ShouldBe(1);
        stub.Plans[0].Targets.Single().SecurityId.ShouldBe("1333");
    }

    [Fact]
    public async Task A_refused_token_makes_the_day_unavailable_and_later_days_make_no_request()
    {
        var (source, stub) = Dhan(p => new IntradayResult(Target.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Unauthorized, "refused")));

        var first = await source.GetAsync(Symbol, Date, TestContext.Current.CancellationToken);
        var later = await source.GetAsync(Symbol, Date.AddDays(1), TestContext.Current.CancellationToken);

        first.Bars.ShouldBeNull();
        first.Unavailable.ShouldNotBeNull().ShouldContain("refused the access token");
        later.Bars.ShouldBeNull();
        stub.Plans.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_rate_limited_failure_an_empty_answer_and_a_partial_day_are_unavailable_without_an_exception()
    {
        var (failed, _) = Dhan(p => new IntradayResult(Target.InstrumentId, [], 2, new IntradayFailure(IntradayFailureKind.Failed, "window: Dhan answered HTTP 429")));
        var (empty, _) = Dhan(p => new IntradayResult(Target.InstrumentId, [], 1));
        var (partial, _) = Dhan(p => new IntradayResult(Target.InstrumentId, [.. DhanDay().Where(b => b.EpochSeconds >= Epoch("10:30"))], 1));

        (await failed.GetAsync(Symbol, Date, TestContext.Current.CancellationToken)).Unavailable.ShouldBe("the fetch failed");
        (await empty.GetAsync(Symbol, Date, TestContext.Current.CancellationToken)).Unavailable.ShouldBe("Dhan answered with no bars");
        (await partial.GetAsync(Symbol, Date, TestContext.Current.CancellationToken)).Unavailable.ShouldBe("Dhan's answer is partial");
    }

    [Fact]
    public async Task An_untracked_symbol_is_unavailable_and_asks_nothing()
    {
        var (source, stub) = Dhan(p => throw new InvalidOperationException("must not be called"));

        var day = await source.GetAsync("NSE:ZZZ", Date, TestContext.Current.CancellationToken);

        day.Unavailable.ShouldNotBeNull();
        stub.Plans.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_folder_source_reads_a_vendor_file_with_iso_times_and_names_a_missing_file()
    {
        using var temp = new TempFolder();
        var lines = BuyDay().Select(b => $"{DateTimeOffset.FromUnixTimeSeconds(b.EpochSeconds).ToOffset(TimeSpan.FromMinutes(330)):yyyy-MM-ddTHH:mm:sszzz},{b.Open},{b.High},{b.Low},{b.Close},{b.Volume}");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "AAA_2026-03-02.csv"), "time,open,high,low,close,volume\n" + string.Join('\n', lines), TestContext.Current.CancellationToken);
        var source = new FolderSecondSource(temp.Path);

        var day = await source.GetAsync(Symbol, Date, TestContext.Current.CancellationToken);
        var none = await source.GetAsync("NSE:BBB", Date, TestContext.Current.CancellationToken);

        day.Bars.ShouldNotBeNull();
        day.Bars.ShouldBe(BuyDay());
        none.Unavailable.ShouldBe("no file BBB_2026-03-02.csv");
    }

    [Fact]
    public void The_cache_round_trips_and_a_text_that_is_not_a_cache_is_refused()
    {
        var days = new Dictionary<(string, DateOnly), AssistBarsCsv.CachedDay> { [(Symbol, Date)] = new("dhan", BuyDay()) };

        var text = AssistBarsCsv.WriteCache(days);
        var back = AssistBarsCsv.ReadCache(text)!;

        back[(Symbol, Date)].Source.ShouldBe("dhan");
        back[(Symbol, Date)].Bars.ShouldBe(BuyDay());
        AssistBarsCsv.WriteCache(back).ShouldBe(text);
        AssistBarsCsv.ReadCache("a,b\n1,2\n").ShouldBeNull();
    }

    [Theory]
    [InlineData("--assist /p")]
    [InlineData("--assist /p --second-source-folder /s")]
    public void The_assist_mode_parses_with_its_folder_only(string line)
    {
        var parsed = VerifyPackOptions.Parse(line.Split(' '), ["ORB"]);

        parsed.Errors.ShouldBeEmpty();
        parsed.Options!.AssistPath.ShouldBe("/p");
    }

    [Theory]
    [InlineData("--assist /p --seed x", "'--assist' is used with")]
    [InlineData("--assist /p --gross", "'--assist' is used with")]
    [InlineData("--second-source-folder /s --strategies ORB", "'--second-source-folder' is used with '--assist'")]
    public void The_assist_mode_refuses_other_options(string line, string message)
    {
        var parsed = VerifyPackOptions.Parse(line.Split(' '), ["ORB"]);

        parsed.Options.ShouldBeNull();
        parsed.Errors.ShouldContain(e => e.Contains(message, StringComparison.Ordinal));
    }
}
