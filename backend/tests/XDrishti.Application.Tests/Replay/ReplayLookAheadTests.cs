using XDrishti.Application.Replay;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay;

/// <summary>
/// SR-01 AC 4 (look-ahead) and AC 16 (deterministic): bars after a session change nothing the replay says about that session, as in P3-08. The honest run doctors every bar after the cut and compares each session up to it; the negative control doctors a bar inside the compared
/// part and must be seen, on ten seeds.
/// </summary>
public sealed class ReplayLookAheadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateOnly Cut = new(2026, 10, 8); // the fourth session of the run

    private static ReplayWorld World()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB", "CCC", "DDD");
        world.Rising("AAA", 0.7);
        world.Rising("BBB", 2.0);
        world.Rising("CCC", 3.3);
        world.Rising("DDD", 4.9);
        return world;
    }

    private static async Task<ReplayRunResult> Run(ReplayWorld world)
    {
        var result = await ReplayRig.Create(world).Driver.RunAsync(ReplayRig.Params(8), Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    private static string Print(ReplaySessionRecord r, bool withFills) => string.Join(
        "|",
        r.Session, string.Join(',', r.Candidates), string.Join(',', r.Ranked), string.Join(',', r.Taken), string.Join(',', r.Blocked), string.Join(',', r.Exits), r.Equity, r.DrawdownPct, r.OpenRiskPct, r.OpenPositions, string.Join(',', r.Notes),
        withFills ? string.Join(',', r.Fills) : string.Empty);

    /// <summary>Every session up to the cut, whole; the session of the cut without its fills (they are dated after it).</summary>
    private static List<string> UpToTheCut(ReplayRunResult run) => [.. run.Sessions.Where(s => s.Session <= Cut).Select(s => Print(s, s.Session < Cut))];

    private static void Doctor(ReplayWorld world, Func<DateOnly, bool> which, int seed)
    {
        var random = new Random(seed);
        foreach (var series in world.Close.Values)
        {
            foreach (var day in series.Keys.Where(which).ToList())
            {
                series[day] = 40 + (random.NextDouble() * 120);
            }
        }
    }

    [Fact]
    public async Task Bars_after_the_cut_change_nothing_the_replay_says_about_the_sessions_up_to_it()
    {
        var honest = UpToTheCut(await Run(World()));
        honest.Count.ShouldBe(4);
        honest.ShouldAllBe(s => s.Contains("AAA", StringComparison.Ordinal)); // the sessions have candidates and plans: the comparison is not of empty things

        for (var seed = 1; seed <= 10; seed++)
        {
            var world = World();
            Doctor(world, d => d > Cut, seed);
            var doctored = await Run(world);

            UpToTheCut(doctored).ShouldBe(honest, "seed " + seed);
            (await Run(World())).Sessions.Where(s => s.Session > Cut).Select(s => Print(s, true)).ToList()
                .ShouldNotBe([.. doctored.Sessions.Where(s => s.Session > Cut).Select(s => Print(s, true))], "the doctoring reaches the later sessions: the comparison is not vacuous");
        }
    }

    [Fact]
    public async Task Negative_control_a_bar_inside_the_compared_part_is_seen_on_every_seed()
    {
        var honest = UpToTheCut(await Run(World()));

        for (var seed = 1; seed <= 10; seed++)
        {
            var world = World();
            Doctor(world, d => d == Cut, seed); // the close of the cut itself: the fill of the session before it, and the plan of the cut, read it
            var doctored = await Run(world);

            UpToTheCut(doctored).ShouldNotBe(honest, "seed " + seed);
        }
    }

    [Fact]
    public async Task Two_runs_of_the_same_input_give_the_same_result()
    {
        var first = await Run(World());
        var second = await Run(World());

        first.Sessions.Select(s => Print(s, true)).ShouldBe([.. second.Sessions.Select(s => Print(s, true))]);
        first.Final.ShouldBe(second.Final);
    }
}
