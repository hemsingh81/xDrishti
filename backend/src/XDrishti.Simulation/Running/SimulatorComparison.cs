using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;
using XDrishti.Strategies;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Running;

/// <summary>
/// One scan, N walks (spec P4-05, D2): a strategy whose exits are N candidates, each checked and built as <see cref="Simulator.Create"/> builds a plain run, so every refusal of P4-02 to P4-04 stands, and one side with the exits of all candidates,
/// whose exit tape holds the union of the features they read. <see cref="Run"/> scans each side once with that union side and walks every entry candidate once per member with that member's own exit rule, which
/// finds its own column of the tape by name: a member reads no feature that only another asked for. The walks are not gated (D4: every candidate sees every entry). Pure and deterministic like <see cref="Simulator"/>, and safe to share.
/// </summary>
public sealed class SimulatorComparison
{
    private readonly Simulator[] _members;
    private readonly CompiledSide[] _union;

    private SimulatorComparison(Simulator[] members, CompiledSide[] union)
    {
        _members = members;
        _union = union;
    }

    /// <summary>The simulators of the candidates, one exit each, in the order of the candidates.</summary>
    public IReadOnlyList<Simulator> Members => _members;

    /// <summary>
    /// Builds the comparison. <paramref name="members"/> is the same strategy once per candidate with that candidate's exit alone; each is checked as <see cref="Simulator.Create"/> checks a plain run, and a candidate the simulator cannot run is
    /// refused by name, with <c>candidates[i]</c> at the start of the path of its problems. The side the one scan uses is the first member's with the exits of the others added (<see cref="CompiledSide.WithExitsOf"/>), so its tape holds the union of
    /// the features they read.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="members"/> is null.</exception>
    /// <exception cref="ArgumentException">Fewer than two members.</exception>
    public static ComparisonBuild Create(IReadOnlyList<StrategyDefinition> members, Timeframe evaluationTimeframe, FillRules? fills = null)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count < 2)
        {
            throw new ArgumentException("A comparison needs two candidates or more.", nameof(members));
        }

        var problems = new List<LocatedProblem>();
        var built = new List<Simulator>(members.Count);
        for (var i = 0; i < members.Count; i++)
        {
            var build = Simulator.Create(members[i], evaluationTimeframe, fills);
            problems.AddRange(build.Problems.Select(p => p with { Path = "candidates[" + i + "]." + p.Path }));
            if (build.Simulator is { } simulator)
            {
                built.Add(simulator);
            }
        }

        var distinct = problems.Distinct().OrderBy(p => p.Problem.Severity == ProblemSeverity.Error ? 0 : 1).ToList();
        if (distinct.Any(p => p.Problem.Severity == ProblemSeverity.Error) || built.Count != members.Count)
        {
            return new ComparisonBuild(null, distinct.AsReadOnly());
        }

        var union = new CompiledSide[built[0].SideRuns.Count];
        for (var s = 0; s < union.Length; s++)
        {
            union[s] = built[0].SideRuns[s].Compiled.WithExitsOf(built.Skip(1).Select(m => m.SideRuns[s].Compiled));
        }

        return new ComparisonBuild(new SimulatorComparison([.. built], union), distinct.AsReadOnly());
    }

    /// <summary>Scans the minutes of one instrument once per side and walks every entry candidate under every member. Same arguments and same refusals as <see cref="Simulator.Run"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="bars"/> or <paramref name="tick"/> is null.</exception>
    public Result<ComparisonResult> Run(InstrumentRef instrument, IReadOnlyList<Bar> bars, TickSpec tick, TradingCalendar? calendar = null, Func<decimal, decimal, long>? quantity = null, ScanContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(tick);
        if (_union.Any(s => s.NeedsContext) && context is null)
        {
            return Result.Failure<ComparisonResult>(Simulator.ContextNotGivenError());
        }

        if (Simulator.NotAscending(bars) is { } unordered)
        {
            return Result.Failure<ComparisonResult>(unordered);
        }

        var outcomes = _members.Select(_ => new List<CandidateOutcome>()).ToArray();
        var walks = _members.Select(_ => WalkCounters.Empty).ToArray();
        var scans = new List<ScanCounters>(_union.Length);
        for (var s = 0; s < _union.Length; s++)
        {
            var created = EntryScanner.Create(_union[s], instrument, calendar, context);
            if (created.IsFailure)
            {
                return Result.Failure<ComparisonResult>(created.Error);
            }

            var scanner = created.Value;
            for (var i = 0; i < bars.Count; i++)
            {
                scanner.Step(bars[i]);
            }

            scans.Add(scanner.Counters);
            for (var k = 0; k < _members.Length; k++)
            {
                var walked = _members[k].WalkSide(_members[k].SideRuns[s], scanner, bars, tick, calendar, quantity, gated: false);
                if (walked.IsFailure)
                {
                    return Result.Failure<ComparisonResult>(walked.Error);
                }

                foreach (var outcome in walked.Value)
                {
                    outcomes[k].Add(outcome);
                    walks[k] = walks[k].Add(outcome.Walk.ToCounters());
                }
            }
        }

        var shared = scans.AsReadOnly();
        var results = new SimulationResult[_members.Length];
        for (var k = 0; k < results.Length; k++)
        {
            var ordered = outcomes[k].OrderBy(o => o.Candidate.DecisionEpochSeconds).ThenBy(o => o.Candidate.Side).ToList().AsReadOnly();
            results[k] = new SimulationResult(_members[k].HeaderOf(), ordered, shared, walks[k]);
        }

        return Result.Success(new ComparisonResult(results, shared));
    }
}
