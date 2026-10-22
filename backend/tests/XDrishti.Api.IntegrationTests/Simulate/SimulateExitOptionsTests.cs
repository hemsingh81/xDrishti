using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// Criterion 2 of P4-05 at the command line: <c>--exit</c> (repeatable) and <c>--time-stop</c> on <c>simulate</c> and <c>verify-pack</c>. Every one of the seven policies is accepted, a list makes candidates, and each refusal is a usage error that
/// names the flag and the value (exit code 2 at the verb, tested in <see cref="SimulateDatabaseTests"/>). Without <c>--exit</c> the options are the options of before.
/// </summary>
public sealed class SimulateExitOptionsTests
{
    private static readonly string[] Known = ["ORB"];

    private const string Base = "--strategy ORB --symbols RELIANCE --from 2026-03-02 --to 2026-03-04 --gross";

    private static SimulateParse Parse(string line) => SimulateOptions.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), Known);

    [Theory]
    [InlineData("FIXED_RR:rr=2", 1)]
    [InlineData("PARTIAL_BE:book_pct=50,t1_rr=1,t2_rr=2", 1)]
    [InlineData("PARTIAL_TRAIL:book_pct=50,t1_rr=1,trail_atr_mult=2,atr_period=3", 1)]
    [InlineData("ATR_TRAIL:mult=2,activate_at_rr=0.5,atr_period=5", 1)]
    [InlineData("STRUCTURE_TRAIL:lookback_bars=3", 1)]
    [InlineData("EMA_TRAIL:length=5", 1)]
    [InlineData("PYRAMID:initial_pct=50,add_at_rr=0.25|0.5,add_pct=30|20,max_adds=2,trail_atr_mult=2,atr_period=3", 1)]
    [InlineData("FIXED_RR:rr=1.5|2|2.5", 3)]
    public void Every_policy_is_accepted_and_a_list_makes_candidates(string spec, int candidates)
    {
        var parsed = Parse(Base + " --exit " + spec);

        parsed.Errors.ShouldBeEmpty();
        parsed.Options!.Exits!.Candidates.Count.ShouldBe(candidates);
    }

    [Fact]
    public void Exit_is_repeatable_and_the_time_stop_is_an_overlay_for_all_of_them()
    {
        var options = Parse(Base + " --exit FIXED_RR:rr=1.5|2|2.5 --exit PARTIAL_BE:book_pct=50,t1_rr=1,t2_rr=2|3 --time-stop min_progress_rr=0.5,intraday_bars=6").Options!;

        options.Exits!.Candidates.Count.ShouldBe(5);
        options.Exits.TimeStop.ShouldBe(new XDrishti.Domain.Research.TimeStopChoice(0.5, 6, 0));
        options.Exits.Candidates.Select(options.Exits.IdOf).First().ShouldBe("FIXED_RR{rr=1.5}+time_stop");
    }

    [Fact]
    public void Without_exit_the_options_are_the_options_of_before()
    {
        Parse(Base).Options!.Exits.ShouldBeNull();
        Parse(Base + " --param rr=3").Options!.Exits.ShouldBeNull();
    }

    public static TheoryData<string, string> Refused => new()
    {
        { "--exit FIXED_RRR:rr=2", "'--exit FIXED_RRR:rr=2'" },
        { "--exit FIXED_RR:risk=2", "'--exit FIXED_RR:risk=2'" },
        { "--exit PARTIAL_BE:book_pct=50|60,t1_rr=1,t2_rr=2", "'book_pct' takes one value" },
        { "--exit EMA_TRAIL:length=5|9", "'--exit EMA_TRAIL:length=5|9'" },
        { "--exit FIXED_RR:rr=2 --param rr=3", "'--param rr' and '--exit'" },
        { "--exit EVENT_EXIT:days_before_results=1", "'--exit EVENT_EXIT" },
        { "--exit FIXED_RR:rr=2 --exit FIXED_RR:rr=2", "FIXED_RR{rr=2} twice" },
        { "--exit FIXED_RR:rr=abc", "'abc' is not" },
        { "--time-stop min_progress_rr=0.5,intraday_bars=6", "'--time-stop' needs at least one '--exit'" },
        { "--exit FIXED_RR:rr=2 --time-stop min_progress_rr=0.5", "'--time-stop min_progress_rr=0.5'" },
        { "--exit FIXED_RR:rr=2 --time-stop event_exit=1", "'--time-stop event_exit=1'" },
        { "--exit FIXED_RR:rr=2 --time-stop min_progress_rr=0.5,intraday_bars=6 --time-stop min_progress_rr=1,intraday_bars=2", "'--time-stop' is given more than once" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_refused_exit_is_a_usage_error_that_names_the_flag_and_the_value(string flags, string expected)
    {
        var parsed = Parse(Base + " " + flags);

        parsed.Options.ShouldBeNull();
        parsed.Errors.ShouldContain(e => e.Contains(expected, StringComparison.Ordinal), string.Join(" | ", parsed.Errors));
    }

    [Fact]
    public void The_list_of_strategies_is_used_alone_so_an_exit_with_it_is_refused()
    {
        var parsed = Parse("--list-strategies --exit FIXED_RR:rr=2");

        parsed.Options.ShouldBeNull();
        parsed.Errors.ShouldContain("'--list-strategies' is used alone");
    }

    [Fact]
    public void The_usage_text_names_the_two_flags()
    {
        SimulateOptions.Usage.ShouldContain("--exit");
        SimulateOptions.Usage.ShouldContain("--time-stop");
        VerifyPackOptions.Usage.ShouldContain("--exit");
        VerifyPackOptions.Usage.ShouldContain("--time-stop");
    }

    private const string Pack = "--strategies ORB --symbols RELIANCE --from 2026-03-02 --to 2026-03-04 --seed s1 --gross";

    [Fact]
    public void A_pack_takes_one_exit_for_the_hand_check_of_a_policy_and_refuses_a_comparison()
    {
        var one = VerifyPackOptions.Parse((Pack + " --exit PARTIAL_BE:book_pct=50,t1_rr=1,t2_rr=2 --time-stop min_progress_rr=0.5,intraday_bars=4").Split(' '), Known);
        var many = VerifyPackOptions.Parse((Pack + " --exit FIXED_RR:rr=2|3").Split(' '), Known);
        var param = VerifyPackOptions.Parse((Pack + " --exit FIXED_RR:rr=2 --param rr=3").Split(' '), Known);

        one.Errors.ShouldBeEmpty();
        one.Options!.Exits!.Candidates.ShouldHaveSingleItem().Id.ShouldBe("PARTIAL_BE{book_pct=50,t1_rr=1,t2_rr=2}");
        many.Options.ShouldBeNull();
        many.Errors.ShouldContain(e => e.Contains("a pack checks one exit set", StringComparison.Ordinal));
        param.Options.ShouldBeNull();
        VerifyPackOptions.Parse(Pack.Split(' '), Known).Options!.Exits.ShouldBeNull();
    }
}
