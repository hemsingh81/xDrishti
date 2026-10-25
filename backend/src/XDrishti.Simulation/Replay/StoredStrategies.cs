using System.Collections.ObjectModel;
using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Running;
using XDrishti.Strategies;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Mirror;
using XDrishti.Strategies.Model;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// Stored strategies (spec P3-02d, ADR 0056): a strategy is its text. <see cref="Inspect"/> reads the text with the strategy file library, takes the canonical text and the hash and compiles every declared timeframe, so a bad text is refused
/// before it is stored; <see cref="Prepare"/> checks the text against the hash the store holds for it (a mismatch is never run), puts the given values in the place of the declared parameters and prepares the definition exactly as a
/// built-in is prepared, so the run record has the same fields. Pure and stateless: the text is read again at each call (once per run).
/// </summary>
public static class StoredStrategies
{
    /// <summary>The text does not read or compile to the hash the store holds for it: the row or the reader is wrong, nothing runs.</summary>
    public const string HashMismatchCode = "strategy_store.hash_mismatch";

    private static readonly SourceSpan NoText = new(0, 0, 1, 1);

    /// <summary>Reads, hashes and compiles the text; the description when it passed, otherwise the located problems (errors only; a warning does not refuse).</summary>
    public static StoredInspection Inspect(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        var (definition, readProblems) = Read(sourceText);
        if (definition is null)
        {
            return new StoredInspection(null, readProblems);
        }

        var problems = new List<LocatedProblem>();
        foreach (var timeframe in definition.Timeframes)
        {
            var build = Simulator.Create(definition, timeframe, null);
            problems.AddRange(build.Problems.Where(p => p.Problem.Severity == ProblemSeverity.Error).Select(p => p with { Path = timeframe.ToCode() + ": " + p.Path }));
        }

        if (problems.Count > 0)
        {
            return new StoredInspection(null, problems);
        }

        return new StoredInspection(Describe(definition), []);
    }

    /// <summary>Reads and compiles the text like <see cref="Inspect"/> but keeps every problem, warnings included, each with the place of its field in the file (spec P5-01). Never throws on any text.</summary>
    public static StoredCheck Check(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        var read = StrategyFile.Read(sourceText);
        var issues = read.Problems.Select(p => new StoredIssue(p.Path, p.Code, p.Message, SeverityName(p.Severity), p.Line, p.Column, p.EndLine, p.EndColumn, null, null, null)).ToList();
        if (read.Definition is not { } definition || read.HasErrors)
        {
            return new StoredCheck(null, read.Envelope.Version, issues);
        }

        var (expanded, mirror) = StrategyMirror.Expand(definition, StrategyFile.WriteSell);
        foreach (var located in mirror)
        {
            // a mirror problem sits in the SELL block that is not in the file, so it is placed at the BUY item it came from
            var problem = located.Problem;
            var at = Locate(read, located.Path.StartsWith("sell.", StringComparison.Ordinal) ? "buy" + located.Path[4..] : located.Path);
            var item = problem.Span.Length > 0;
            issues.Add(new StoredIssue(located.Path, problem.Code, problem.Message, SeverityName(problem.Severity), at?.Line, at?.Column, at?.EndLine, at?.EndColumn, item ? problem.Span.Start : null, item ? problem.Span.Length : null, problem.Suggestion));
        }

        if (mirror.Any(m => m.Problem.Severity == ProblemSeverity.Error))
        {
            return new StoredCheck(null, read.Envelope.Version, issues);
        }

        definition = expanded;
        var compiled = new List<StoredIssue>();
        foreach (var timeframe in definition.Timeframes)
        {
            foreach (var located in Simulator.Create(definition, timeframe, null).Problems)
            {
                var problem = located.Problem;
                var at = Locate(read, located.Path);
                var item = problem.Span.Length > 0 || problem.Span.Start > 0;
                var issue = new StoredIssue(located.Path, problem.Code, problem.Message, SeverityName(problem.Severity), at?.Line, at?.Column, at?.EndLine, at?.EndColumn, item ? problem.Span.Start : null, item ? problem.Span.Length : null, problem.Suggestion);
                if (!compiled.Contains(issue))
                {
                    compiled.Add(issue);
                }
            }
        }

        issues.AddRange(compiled);
        return new StoredCheck(compiled.Any(i => i.Severity == "error") ? null : Describe(definition), read.Envelope.Version, issues);
    }

    /// <summary>The text with its strategy id replaced, written in the fixed layout of the file (spec P5-01, clone); null when the text does not read.</summary>
    public static string? Retarget(string sourceText, string newId)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(newId);
        var (definition, _) = Read(sourceText, expand: false);
        return definition is null ? null : StrategyFile.Write(definition with { Id = newId });
    }

    /// <summary>The JSON Schema of the strategy file, for the editor.</summary>
    public static string SchemaJson() => StrategyFileSchema.Json();

    private static string SeverityName(ProblemSeverity severity) => severity switch
    {
        ProblemSeverity.Error => "error",
        ProblemSeverity.Warning => "warning",
        _ => "information",
    };

    /// <summary>The place of the field at the model path, or of the nearest enclosing field when the path itself is not in the file.</summary>
    private static FilePosition? Locate(FileReadResult read, string path)
    {
        var current = path;
        while (current.Length > 0)
        {
            if (read.Locate(current) is { } found)
            {
                return found;
            }

            var cut = Math.Max(current.LastIndexOf('.'), current.LastIndexOf('['));
            current = cut < 0 ? string.Empty : current[..cut];
        }

        return read.Locate(string.Empty);
    }

    private static StoredDescription Describe(StrategyDefinition definition)
    {
        var json = CanonicalStrategy.Json(definition);
        return new StoredDescription(
            definition.Id,
            definition.Style == Style.Swing ? "swing" : "intraday",
            CanonicalText.Sha256Hex(json),
            json,
            definition.Timeframes,
            definition.Sides,
            [.. definition.Params.Select(p => new KeyValuePair<string, double>(p.Name, p.Value))]);
    }

    /// <summary>Prepares the stored text for a run: the values replace the declared parameters (a name not declared is <c>simulation.unknown_parameter</c>), the compile judges them.</summary>
    /// <param name="sourceText">The stored text.</param>
    /// <param name="expectedHash">The hash the store holds for it.</param>
    /// <param name="parameters">Values by declared name; null or empty keeps the declared values.</param>
    /// <param name="evaluationTimeframe">The timeframe the rules are evaluated on.</param>
    /// <param name="fills">The fill rules; null means the defaults.</param>
    /// <param name="exits">The exits to run in place of the strategy's own (P4-05); null keeps them.</param>
    public static PreparedBuild Prepare(string sourceText, string expectedHash, IReadOnlyDictionary<string, double>? parameters, Timeframe evaluationTimeframe, FillRules? fills, ExitChoice? exits)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(expectedHash);
        var (definition, readProblems) = Read(sourceText);
        if (definition is null && readProblems.Any(p => p.Problem.Code.StartsWith("strategy.mirror.", StringComparison.Ordinal)))
        {
            return new PreparedBuild(null, readProblems);
        }

        if (definition is null)
        {
            return new PreparedBuild(null, [Located(string.Empty, HashMismatchCode, "The stored text no longer reads; the library that reads it has changed. Nothing ran.")]);
        }

        if (!string.Equals(CanonicalText.Sha256Hex(CanonicalStrategy.Json(definition)), expectedHash, StringComparison.Ordinal))
        {
            return new PreparedBuild(null, [Located(string.Empty, HashMismatchCode, "The stored text does not give the hash the store holds for it; the row or the reader is wrong. Nothing ran.")]);
        }

        var given = parameters ?? new Dictionary<string, double>();
        var names = definition.Params.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = given.Keys.Where(k => !names.Contains(k)).Order(StringComparer.Ordinal)
            .Select(k => Located("parameters." + k, SimulationErrors.UnknownParameterCode, "The strategy `" + definition.Id + "` has no parameter `" + k + "`; its parameters are " + NameList(names) + ".")).ToList();
        if (unknown.Count > 0)
        {
            return new PreparedBuild(null, unknown);
        }

        var effective = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var parameter in definition.Params)
        {
            effective[parameter.Name] = given.TryGetValue(parameter.Name, out var value) ? value : parameter.Value;
        }

        // StrategyPreparation throws on a value that is not finite; that is the caller's mistake here, so it is a located problem
        foreach (var (name, value) in effective)
        {
            if (!double.IsFinite(value))
            {
                return new PreparedBuild(null, [Located("parameters." + name, SimulationErrors.ParameterOutOfRangeCode, "The parameter `" + name + "` must be a finite number.")]);
            }
        }

        var substituted = definition with { Params = [.. definition.Params.Select(p => p with { Value = effective[p.Name] })] };
        return StrategyPreparation.PrepareWithExits(substituted, new ReadOnlyDictionary<string, double>(effective), evaluationTimeframe, fills, exits);
    }

    private static string NameList(IEnumerable<string> names) => string.Join(", ", names.Order(StringComparer.Ordinal).Select(n => "`" + n + "`"));

    private static (StrategyDefinition? Definition, List<LocatedProblem> Problems) Read(string sourceText, bool expand = true)
    {
        var read = StrategyFile.Read(sourceText);
        if (read.Definition is { } definition && !read.HasErrors)
        {
            if (!expand)
            {
                return (definition, []);
            }

            // the automatic SELL (spec P3-02e): the run sees the mirrored block, so the hash and the run key hold it; the stored text is not changed
            var (expanded, mirror) = StrategyMirror.Expand(definition);
            var errors = mirror.Where(m => m.Problem.Severity == ProblemSeverity.Error).ToList();
            return errors.Count > 0 ? (null, errors) : (expanded, []);
        }

        return (null, [.. read.Problems.Where(p => p.Severity == ProblemSeverity.Error).Select(p => Located(p.Path, p.Code, "line " + p.Line.ToString(CultureInfo.InvariantCulture) + ": " + p.Message))]);
    }

    private static LocatedProblem Located(string path, string code, string message) => new(path, new Problem(code, message, NoText));
}
