#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Ai;
using XDrishti.Application.Ai.Briefing;
using XDrishti.Application.Ai.Drafting;
using XDrishti.Application.Assistant.Evaluation;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Assistant.Drafting;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Ai;

/// <summary>
/// The ports of the eval as fakes with counters (spec P9-09 test plan): the real files of the repo's <c>eval/</c> folder, a model that answers every question perfectly (built from the expected parts of the case), a briefing writer that returns the fixed-format text, a draft pipeline that
/// returns what each idea's assertions ask for, and an in-memory store. A refusal must not reach the model, and nothing here calls a real model.
/// </summary>
internal sealed class EvalRig
{
    public AssistantSettings Settings { get; set; } = new(7, true, false, 6);

    public string? Marker { get; set; }

    public EvalModelInfo Model { get; set; } = new(true, LlmState.Ready, "Qwen3-30B-A3B-Q4_K_M.gguf", new string('a', 64), "b1");

    public List<PromptStamp> Prompts { get; } = [.. PromptRegistry.All.Select(p => new PromptStamp(p.Name, p.Version, p.Sha256))];

    public bool NightlyRunning { get; set; }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.FromHours(5.5)));

    public TurnGate Gate { get; } = new();

    public TurnOptions Turn { get; } = new();

    public MemoryEvalStore Store { get; } = new();

    public EvalWorld World { get; }

    public PerfectAnswerSource Answers { get; }

    public int WriterCalls { get; private set; }

    public int DrafterCalls { get; private set; }

    public Func<string, string, string?>? Wrong { get; set; }

    public EvalRig()
    {
        var files = DiskEvalFiles.Read();
        World = EvalWorld.Load(files).Value;
        Marker = World.FixtureHash;
        Answers = new PerfectAnswerSource(World.Data);
    }

    public RunEvalHandler Run()
    {
        var settings = Substitute.For<IAssistantSettingsStore>();
        settings.CurrentAsync(Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Settings));
        var nightly = Substitute.For<INightlyRunProbe>();
        nightly.IsRunningAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(NightlyRunning));
        return new RunEvalHandler(new DiskEvalFiles(), new FakeProbe(this), settings, new FakeEnvironment(this), new FakePrompts(this), Answers, PlanRunner(), IdeaRunner(), Store, nightly, Turn, Gate, Time, NullLogger<RunEvalHandler>.Instance);
    }

    public PlanTextRunner PlanRunner() => new(new FakeWriter(this), new BriefingOptions { UseModel = false });

    public IdeaDraftRunner IdeaRunner()
    {
        var ideas = new PerfectIdeas(World.Ideas);
        var drafter = Substitute.For<IStrategyDrafter>();
        drafter.DraftAsync(Arg.Any<DraftModelRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            DrafterCalls++;
            return ValueTask.FromResult(new DraftModelAnswer(ideas.AnswerFor(call.Arg<DraftModelRequest>().User), 100, 50, "stop", 10));
        });
        var sheet = Substitute.For<IDraftSheetSource>();
        sheet.Sheet.Returns("SHEET");
        sheet.Hash.Returns(new string('s', 64));
        var submit = Substitute.For<IQueryHandler<SubmitDraftQuery, DraftEvaluation>>();
        submit.HandleAsync(Arg.Any<SubmitDraftQuery>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult(PerfectIdeas.Evaluate(call.Arg<SubmitDraftQuery>())));
        var reader = Substitute.For<IStrategyTextReader>();
        reader.ToJson(Arg.Any<string>()).Returns(call => ideas.TreeOf(call.Arg<string>()));
        return new IdeaDraftRunner(sheet, drafter, submit, reader, new DraftOptions { MaxRepairs = 2 });
    }

    public AcceptEvalHandler Accept() => new(Store, Time, NullLogger<AcceptEvalHandler>.Instance);

    public GetEvalStatusHandler Status() => new(new DiskEvalFiles(), Store, new FakePrompts(this), new FakeEnvironment(this));

    public sealed class FakeProbe(EvalRig rig) : IEvalFixtureProbe
    {
        public ValueTask<string?> ReadFixtureHashAsync(CancellationToken cancellationToken) => ValueTask.FromResult(rig.Marker);
    }

    public sealed class FakeEnvironment(EvalRig rig) : IEvalEnvironment
    {
        public ValueTask<EvalModelInfo> ModelAsync(CancellationToken cancellationToken) => ValueTask.FromResult(rig.Model);
    }

    public sealed class FakePrompts(EvalRig rig) : IPromptVersions
    {
        public IReadOnlyList<PromptStamp> All => rig.Prompts;
    }

    public sealed class FakeWriter(EvalRig rig) : IBriefingWriter
    {
        public ValueTask<BriefingWrite> WriteAsync(BriefingRequest request, CancellationToken cancellationToken)
        {
            rig.WriterCalls++;
            var plan = rig.World.Plans.First(p => request.User.Contains(p.Facts.Render(), StringComparison.Ordinal));
            var text = rig.Wrong?.Invoke("plans", plan.Id) ?? DeterministicBriefing.Build(plan.Facts);
            return ValueTask.FromResult(new BriefingWrite(text, 100, 50, "stop", 10));
        }
    }
}

/// <summary>Reads the repo's <c>eval/</c> folder as the application would.</summary>
internal sealed class DiskEvalFiles : IEvalFiles
{
    public static EvalFileSet Read()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eval", "thresholds.json")))
        {
            dir = dir.Parent;
        }

        var root = Path.Combine(dir!.FullName, "eval");
        string Text(string relative) => File.ReadAllText(Path.Combine(root, relative));
        return new EvalFileSet(Text("fixture/fixture.json"), Text("sets/data-questions.jsonl"), Text("sets/plans.jsonl"), Text("sets/ideas.jsonl"), Text("thresholds.json"));
    }

    public ValueTask<Result<EvalFileSet>> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success(Read()));
}

/// <summary>The in-memory store: insert-only like the real one, with the counters a test reads.</summary>
internal sealed class MemoryEvalStore : IEvalStore
{
    public List<NewEvalRun> Runs { get; } = [];

    public Dictionary<Guid, List<NewEvalCase>> Cases { get; } = [];

    public List<(Guid Run, string By, string? Reason)> Baselines { get; } = [];

    public ValueTask InsertRunAsync(NewEvalRun run, IReadOnlyList<NewEvalCase> cases, CancellationToken cancellationToken)
    {
        Runs.Add(run);
        Cases[run.Id] = [.. cases];
        return ValueTask.CompletedTask;
    }

    public ValueTask<StoredEvalRun?> GetAsync(Guid id, CancellationToken cancellationToken) => ValueTask.FromResult(Runs.Where(r => r.Id == id).Select(Stored).FirstOrDefault());

    public ValueTask<IReadOnlyList<StoredEvalRun>> ListAsync(int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredEvalRun>>([.. Runs.OrderByDescending(r => r.FinishedAt).Take(limit).Select(Stored)]);

    public ValueTask<IReadOnlyList<StoredEvalCase>> CasesAsync(Guid runId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<StoredEvalCase>>([.. Cases.GetValueOrDefault(runId, []).Select((c, i) => new StoredEvalCase(i + 1, runId, c.Set, c.CaseId, c.Repeat, c.Passed, c.Grounded, c.Score, c.DetailJson, c.Answer, c.SessionId, c.ToolCalls, c.LatencyMs, c.PromptTokens, c.CompletionTokens))]);

    public ValueTask<StoredEvalRun?> NewestGatingAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Runs.Where(r => r.Gating).OrderByDescending(r => r.FinishedAt).Select(Stored).FirstOrDefault());

    public ValueTask<StoredEvalBaseline?> CurrentBaselineAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Baselines.Count == 0 ? null : new StoredEvalBaseline(Baselines.Count, Baselines[^1].Run, Baselines[^1].By, Baselines[^1].Reason, DateTimeOffset.UnixEpoch));

    public ValueTask<StoredEvalBaseline?> BaselineOfAsync(Guid runId, CancellationToken cancellationToken)
    {
        var index = Baselines.FindIndex(b => b.Run == runId);
        return ValueTask.FromResult(index < 0 ? null : new StoredEvalBaseline(index + 1, runId, Baselines[index].By, Baselines[index].Reason, DateTimeOffset.UnixEpoch));
    }

    public ValueTask<(StoredEvalBaseline Baseline, bool Created)> InsertBaselineAsync(Guid runId, string acceptedBy, string? reason, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var created = Baselines.All(b => b.Run != runId);
        if (created)
        {
            Baselines.Add((runId, acceptedBy, reason));
        }

        var index = Baselines.FindIndex(b => b.Run == runId);
        return ValueTask.FromResult((new StoredEvalBaseline(index + 1, runId, acceptedBy, reason, at), created));
    }

    private static StoredEvalRun Stored(NewEvalRun r) =>
        new(r.Id, r.StartedAt, r.FinishedAt, r.Target, r.Profile, r.Sets, r.Repeats, r.Gating, r.ModelId, r.ModelSha256, r.RuntimeDigest, r.ParamsJson, r.PromptVersionsJson, r.SetsHash, r.ThresholdsHash, r.FixtureHash, r.Fingerprint, r.Status, r.Verdict, r.SummaryJson, r.Actor);
}

/// <summary>Answers every data question perfectly: the figures of the expected parts, in one sentence that holds the question's anchor, with the same figures in the tool result it "read". A test can make it wrong or make the model go down.</summary>
internal sealed class PerfectAnswerSource(IReadOnlyList<EvalCase> cases) : IEvalAnswerSource
{
    public int Calls { get; private set; }

    public string? LastActor { get; private set; }

    public Func<EvalCase, int, string?>? Override { get; set; }

    public int? FailAfter { get; set; }

    public Error FailWith { get; set; } = new("assistant.model_unavailable", "down", ErrorKind.Upstream);

    public ValueTask<Result<EvalAnswer>> AskAsync(string question, string actor, CancellationToken cancellationToken)
    {
        Calls++;
        LastActor = actor;
        if (FailAfter is { } limit && Calls > limit)
        {
            return ValueTask.FromResult(Result.Failure<EvalAnswer>(FailWith));
        }

        var evalCase = cases.First(c => c.Question == question);
        var (text, result) = Perfect(evalCase);
        text = Override?.Invoke(evalCase, Calls) ?? text;
        return ValueTask.FromResult(Result.Success(new EvalAnswer(text, Guid.NewGuid(), [result], 1, 5, 100, 20, "stop")));
    }

    /// <summary>The perfect answer of a case and the tool result behind it.</summary>
    public static (string Text, string Result) Perfect(EvalCase evalCase)
    {
        var anchor = evalCase.Anchors.Count > 0 ? evalCase.Anchors[0] : "answer";
        var words = new List<string>();
        var figures = new List<string>();
        foreach (var part in evalCase.Parts)
        {
            switch (part)
            {
                case NumberPart n:
                    var printed = n.Value.Round(Math.Max(n.MinDecimals, n.MinDecimals == 0 && !n.Value.EqualsDecimal(n.Value.Round(0)) ? 2 : n.MinDecimals)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    figures.Add(printed);
                    words.Add(printed + (n.Unit == "%" ? "%" : string.Empty));
                    break;
                case IntegerPart i:
                    figures.Add(i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    words.Add(i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case TextPart t:
                    words.Add(t.ContainsAny[0]);
                    break;
                case HonestPart h:
                    words.Add(h.Phrases[0]);
                    break;
                default:
                    break;
            }
        }

        var allowed = evalCase.AllowedNumbers.Select(a => a.Round(3).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return ($"The {anchor}: {string.Join(", ", words)}.", JsonSerializer.Serialize(new { ok = true, figures = figures.Concat(allowed) }));
    }
}

/// <summary>What a perfect assistant would draft for each idea of the set: a text and a tree that hold what the assertions ask for.</summary>
internal sealed class PerfectIdeas
{
    private readonly Dictionary<string, IdeaCase> _byIdea;
    private readonly Dictionary<string, string> _treeByYaml = [];
    private readonly Dictionary<string, string> _yamlByIdea = [];

    public PerfectIdeas(IReadOnlyList<IdeaCase> ideas)
    {
        _byIdea = ideas.ToDictionary(i => i.Idea, StringComparer.Ordinal);
        foreach (var idea in ideas.Where(i => i.Kind == IdeaCase.Draft))
        {
            var yaml = new List<string> { "strategy:", "  id: DRAFT_" + idea.Id.Replace('-', '_') };
            var tree = new Dictionary<string, object?>();
            var sides = new List<string>();
            foreach (var assertion in idea.Assertions)
            {
                switch (assertion)
                {
                    case PathEquals p:
                        Set(tree, p.Path, p.Value);
                        yaml.Add($"  # {p.Path} = {p.Value}");
                        break;
                    case PathExists p:
                        Set(tree, p.Path, "x");
                        yaml.Add($"  # {p.Path}");
                        break;
                    case YamlContainsAny a:
                        yaml.Add("  # " + a.Words[0]);
                        break;
                    case NumberCovered n:
                        yaml.Add("  # " + n.Token);
                        break;
                    case SidesInclude s:
                        sides.AddRange(s.Sides);
                        break;
                    default:
                        break;
                }
            }

            if (sides.Count > 0)
            {
                Set(tree, "strategy.sides", sides.Distinct().ToList());
            }

            var text = string.Join('\n', yaml);
            _yamlByIdea[idea.Idea] = text;
            _treeByYaml[text] = JsonSerializer.Serialize(tree);
        }
    }

    public string AnswerFor(string user)
    {
        var idea = _byIdea.Values.First(i => user.Contains(i.Idea, StringComparison.Ordinal));
        return idea.Kind == IdeaCase.Draft ? DraftRig.Json(_yamlByIdea[idea.Idea]) : DeclineJson(idea);
    }

    public static DraftEvaluation Evaluate(SubmitDraftQuery query) =>
        query.Answer.Decline ? new DraftEvaluation(DraftStatus.Declined, null, [], [], null) : new DraftEvaluation(DraftStatus.Valid, query.Answer.Yaml, [], [], "DRAFT_X");

    public string? TreeOf(string yaml) => _treeByYaml.GetValueOrDefault(yaml);

    private static string DeclineJson(IdeaCase idea)
    {
        var word = idea.Assertions.OfType<UnsupportedMentions>().First().Words[0];
        return "{\"decline\":true,\"unsupported\":[\"" + word + " is not available\"],\"understood\":[],\"assumptions\":[],\"not_expressed\":[],\"requested_test\":{},\"yaml\":null}";
    }

    private static void Set(Dictionary<string, object?> tree, string path, object value)
    {
        var parts = path.Split('.');
        var node = tree;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!node.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> map)
            {
                map = [];
                node[parts[i]] = map;
            }

            node = map;
        }

        node[parts[^1]] = value;
    }
}
