using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Replay;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// SR-01 part A (ADR 0272): the system replay is research only. It names no execution project and no order, Planning does not name it, it reads bars only through the clamped reader, and it calls none of the stores or gates the live nightly writes through.
/// </summary>
public sealed partial class ReplayArchitectureTests
{
    private static readonly System.Reflection.Assembly Application = typeof(SystemReplayOptions).Assembly;
    private static readonly System.Reflection.Assembly Domain = typeof(XDrishti.Domain.Replay.BlockReason).Assembly;
    private static readonly System.Reflection.Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    [GeneratedRegex(@"\.\s*\w*(Store|Append|Insert|Save|Write|Publish|Delete|Update|Move)\w*Async\(", RegexOptions.CultureInvariant)]
    private static partial Regex WriteCall();

    [GeneratedRegex(@"PlaceOrder|IOrder\w*|\bBroker\w*|\bExecution\w*|IExec\w*", RegexOptions.CultureInvariant)]
    private static partial Regex Banned();

    private static IEnumerable<string> Files()
    {
        var src = Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj")))!;
        return new[] { Path.Combine("XDrishti.Domain", "Replay"), Path.Combine("XDrishti.Application", "Replay"), Path.Combine("XDrishti.Infrastructure", "Replay") }
            .SelectMany(d => Directory.GetFiles(Path.Combine(src, d), "*.cs"));
    }

    [Fact]
    public void The_replay_names_no_execution_project_broker_or_order()
    {
        Files().Count().ShouldBeGreaterThan(20, "the scan sees the replay files");
        foreach (var file in Files())
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("XDrishti.Execution", customMessage: file);
            Banned().IsMatch(text).ShouldBeFalse(file);
        }

        foreach (var (assembly, ns) in new[] { (Domain, "XDrishti.Domain.Replay"), (Application, "XDrishti.Application.Replay"), (Infrastructure, "XDrishti.Infrastructure.Replay") })
        {
            var result = Types.InAssembly(assembly).That().ResideInNamespace(ns).ShouldNot().HaveDependencyOn("XDrishti.Execution").GetResult();

            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void Planning_does_not_name_a_replay_type()
    {
        var domain = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Planning").ShouldNot().HaveDependencyOn("XDrishti.Domain.Replay").GetResult();
        var application = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Planning").ShouldNot().HaveDependencyOn("XDrishti.Application.Replay").GetResult();
        var infrastructure = Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.Planning").ShouldNot().HaveDependencyOn("XDrishti.Application.Replay").GetResult();

        domain.IsSuccessful.ShouldBeTrue(string.Join(", ", domain.FailingTypeNames ?? []));
        application.IsSuccessful.ShouldBeTrue(string.Join(", ", application.FailingTypeNames ?? []));
        infrastructure.IsSuccessful.ShouldBeTrue(string.Join(", ", infrastructure.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_replay_reaches_stored_bars_only_through_the_clamped_reader()
    {
        // only ClampedBars calls the bar ports; ReplayFacts holds them just to build it; nothing else of the replay (the session runner, the driver, the candidate source) names a bar port
        var others = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Replay").And().DoNotHaveName("ClampedBars").And().DoNotHaveName("ReplayFacts")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Abstractions.Market.Bars.IBarStore", "XDrishti.Application.Abstractions.Market.Bars.IBarQueries").GetResult();
        var infrastructure = Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.Replay")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Abstractions.Market.Bars.IBarStore", "XDrishti.Application.Abstractions.Market.Bars.IBarQueries").GetResult();
        others.IsSuccessful.ShouldBeTrue(string.Join(", ", others.FailingTypeNames ?? []));
        infrastructure.IsSuccessful.ShouldBeTrue(string.Join(", ", infrastructure.FailingTypeNames ?? []));

        var facts = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Replay", "ReplayFacts.cs"));
        facts.ShouldNotContain("minuteBars.", customMessage: "ReplayFacts must read minute bars through ClampedBars only");
        facts.ShouldNotContain("dayBars.", customMessage: "ReplayFacts must read day bars through ClampedBars only");
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Replay", "ClampedBars.cs")).ShouldContain("ReplayHoldOutException");
    }

    [Fact]
    public void The_replay_calls_no_nightly_gate_no_plan_store_and_no_write()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Replay")
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Application.Planning.RunNightlyPlanHandler", "XDrishti.Application.Planning.INightlyGate", "XDrishti.Application.Abstractions.Planning.IPlanStore",
                "XDrishti.Application.Abstractions.Paper.IPaperStore", "XDrishti.Application.Abstractions.Planning.IGradeStore", "XDrishti.Application.Abstractions.Planning.IJournalStore")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));

        foreach (var file in Files())
        {
            WriteCall().IsMatch(File.ReadAllText(file)).ShouldBeFalse(file);
        }
    }

    [Fact]
    public void The_scans_see_what_they_are_meant_to_see_sentinels()
    {
        // the write-call pattern matches the write verbs a store has, and the bar-port scan sees the one type that may hold them
        WriteCall().IsMatch("await store.AppendAsync(x, ct);").ShouldBeTrue();
        WriteCall().IsMatch("await store.InsertOnceAsync(x, ct);").ShouldBeTrue();
        WriteCall().IsMatch("await plans.UpdateStateAsync(x, ct);").ShouldBeTrue();
        WriteCall().IsMatch("await facts.CellsAsync(p, d, ct);").ShouldBeFalse();
        Types.InAssembly(Application).That().HaveName("ClampedBars").Should().HaveDependencyOn("XDrishti.Application.Abstractions.Market.Bars.IBarStore").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().HaveName("ReplayFacts").Should().HaveDependencyOn("XDrishti.Application.Abstractions.Market.Bars.IBarStore").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_closed_list_of_block_reasons_is_the_eight_of_the_spec()
    {
        XDrishti.Domain.Replay.BlockReasons.All.Select(XDrishti.Domain.Replay.BlockReasons.Code).ShouldBe(["gate", "smart_rule", "max_positions", "total_risk", "cluster_cap", "capital", "data_missing", "no_version"]);
    }
}
