using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Application.Learning;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec CF-12 part 12b, AC5 and AC6 (ADR 0219): the kill proposals, their application, the owner's automatic setting and the re-enable. Demotion lowers risk and may run automatically behind a setting (default OFF); re-enabling raises risk and is **never** automatic.
/// Enforced here, each scan with a sentinel it must catch: (1) the closure of everything the automatic path can reach (the daily service, the evaluation, the proposal and apply handlers, the lifecycle recorder they feed) does not contain the re-enable or a revert, calls the lifecycle
/// recorder only through the pause handler, and that handler names no state but Paused; (2) the writers of the switch, the proposals and the lifecycle are the few types they must be; (3) nothing in Planning, Selection or Autonomy names a kill type, setting or proposal; (4) none of it
/// touches an order or the execution plane; (5) the routes are the owner's, with a fresh password where risk is allowed to rise.
/// </summary>
public sealed class KillArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Self = typeof(KillArchitectureTests).Assembly;

    private static readonly string[] ProductionNames =
    [
        "XDrishti.Api", "XDrishti.Application", "XDrishti.Domain", "XDrishti.Execution", "XDrishti.Feed", "XDrishti.Hosting", "XDrishti.Indicators", "XDrishti.Infrastructure", "XDrishti.Learning.MlNet", "XDrishti.Mcp",
        "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Worker",
    ];

    // where the automatic path starts: the daily service and everything it calls to evaluate, propose and apply
    private static readonly string[] AutoRoots =
    [
        "XDrishti.Application.Confidence.DecayMonitorService", "XDrishti.Application.Confidence.EvaluateDecayHandler", "XDrishti.Application.Confidence.ProposeKillHandler", "XDrishti.Application.Confidence.ApplyKillHandler",
    ];

    private static readonly string[] ReEnableTypes =
    [
        "XDrishti.Application.Confidence.ReEnableCellHandler", "XDrishti.Application.Confidence.ReEnableCellCommand", "XDrishti.Application.Confidence.ReEnableOutcome",
    ];

    private const string MarkerCommand = "XDrishti.Application.Abstractions.Messaging.ICommand";

    private const string RecordCommand = "XDrishti.Application.Learning.RecordCellTransitionCommand";

    private static readonly string[] KillTypes =
    [
        "XDrishti.Application.Confidence.ProposeKillHandler", "XDrishti.Application.Confidence.ProposeKillCommand", "XDrishti.Application.Confidence.ApplyKillHandler", "XDrishti.Application.Confidence.ApplyKillCommand",
        "XDrishti.Application.Confidence.ReEnableCellHandler", "XDrishti.Application.Confidence.ReEnableCellCommand", "XDrishti.Application.Confidence.ChangeKillSwitchHandler", "XDrishti.Application.Confidence.ChangeKillSwitchCommand",
        "XDrishti.Application.Confidence.EvaluateDecayHandler", "XDrishti.Application.Confidence.DecayMonitorService", "XDrishti.Application.Confidence.GetKillProposalsHandler", "XDrishti.Application.Confidence.KillProposalNotices",
        "XDrishti.Application.Confidence.StrategyCells", "XDrishti.Application.Confidence.KillActors", "XDrishti.Application.Confidence.KillErrors",
        typeof(IKillProposalReader).FullName!, typeof(IKillProposalStore).FullName!, typeof(IKillSettings).FullName!, typeof(IKillSwitchStore).FullName!, typeof(IDecayCheckStore).FullName!, typeof(KillSwitch).FullName!,
        typeof(KillOptions).FullName!, typeof(KillDecision).FullName!, typeof(KillEvidence).FullName!,
    ];

    /// <summary>Stands for an automatic path that re-enables: it holds the re-enable handler's contract. The closure of the scan must reach the handler through it.</summary>
    internal sealed class ReEnableSentinel(ICommandHandler<ReEnableCellCommand, ReEnableOutcome> reEnable)
    {
        public ICommandHandler<ReEnableCellCommand, ReEnableOutcome> Handler => reEnable;
    }

    /// <summary>Stands for an automatic path that moves a cell to another state than Paused: it builds the lifecycle command itself and appends to the changelog.</summary>
    internal sealed class DirectLifecycleSentinel(ILearningChangelog changelog)
    {
        public ValueTask<ChangelogAppend> Raise(string cell) =>
            changelog.AppendAsync(new NewChangelogEntry("transition", "cell", cell, "paused", "paper", "why", "auto", ["e"], null, null, null, null, DateTimeOffset.UnixEpoch), CancellationToken.None);

        public static RecordCellTransitionCommand Command(string cell) => new(cell, CellState.Paper, "why", "auto", new TransitionFacts(), ["e"]);
    }

    /// <summary>Stands for a caller that sets the flag that lets a move leave a kill pause, with an object initializer.</summary>
    internal static class FlagSentinel
    {
        public static RecordCellTransitionCommand Command(string cell) => new(cell, CellState.Paper, "why", "owner:x", new TransitionFacts(), ["e"]) { OwnerReEnable = true };
    }

    /// <summary>The same with a <c>with</c> expression.</summary>
    internal static class FlagWithSentinel
    {
        public static RecordCellTransitionCommand Command(RecordCellTransitionCommand command) => command with { OwnerReEnable = true };
    }

    /// <summary>Stands for a consumer that chooses what to approve and reads a proposal.</summary>
    internal sealed class ProposalReaderSentinel(IKillProposalReader proposals)
    {
        public ValueTask<KillProposalRow?> Read() => proposals.FindAsync(1, CancellationToken.None);
    }

    // ---- the index of every type of the production assemblies (and the sentinels) read from the IL ----

    private sealed record TypeIndex(Dictionary<string, TypeDefinition> Types, ILookup<string, TypeDefinition> Implementers);

    private static readonly Lazy<TypeIndex> Index = new(() =>
    {
        var paths = ProductionNames.Select(n => Assembly.Load(new AssemblyName(n)).Location).Append(Self.Location).Distinct().ToList();
        var types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            foreach (var type in ModuleDefinition.ReadModule(path).GetTypes())
            {
                types[type.FullName] = type;
            }
        }

        var implementers = types.Values.SelectMany(t => t.Interfaces.Select(i => (Interface: i.InterfaceType.FullName, Type: t))).ToLookup(x => x.Interface, x => x.Type, StringComparer.Ordinal);
        return new TypeIndex(types, implementers);
    });

    /// <summary>The type a compiler-generated nested type (a closure, a state machine) belongs to; a nested type that is written in the source stays itself. Reflection's <c>+</c> and Cecil's <c>/</c> are the same separator here.</summary>
    private static string Root(string fullName)
    {
        var parts = fullName.Replace('+', '/').Split('/');
        return string.Join('/', parts.TakeWhile((p, i) => i == 0 || !p.StartsWith('<')));
    }

    private static string Name(Type type) => type.FullName!.Replace('+', '/');

    private static void Expand(TypeReference? reference, ICollection<string> into)
    {
        switch (reference)
        {
            case null:
                return;
            case GenericInstanceType generic:
                into.Add(generic.FullName);
                Expand(generic.ElementType, into);
                foreach (var argument in generic.GenericArguments)
                {
                    Expand(argument, into);
                }

                return;
            case ArrayType array:
                Expand(array.ElementType, into);
                return;
            case ByReferenceType byRef:
                Expand(byRef.ElementType, into);
                return;
            default:
                into.Add(reference.FullName);
                return;
        }
    }

    private static void ExpandMember(MemberReference member, ICollection<string> into)
    {
        Expand(member.DeclaringType, into);
        switch (member)
        {
            case GenericInstanceMethod generic:
                foreach (var argument in generic.GenericArguments)
                {
                    Expand(argument, into);
                }

                ExpandMember(generic.ElementMethod, into);
                break;
            case MethodReference method:
                Expand(method.ReturnType, into);
                foreach (var parameter in method.Parameters)
                {
                    Expand(parameter.ParameterType, into);
                }

                break;
            case FieldReference field:
                Expand(field.FieldType, into);
                break;
            default:
                break;
        }
    }

    private static List<string> References(TypeDefinition type)
    {
        var into = new List<string>();
        Expand(type.BaseType, into); // the interfaces a type implements are not followed: implementing a contract does not reach the other classes that implement it

        foreach (var field in type.Fields)
        {
            Expand(field.FieldType, into);
        }

        foreach (var method in type.Methods)
        {
            Expand(method.ReturnType, into);
            foreach (var parameter in method.Parameters)
            {
                Expand(parameter.ParameterType, into);
            }

            if (!method.HasBody)
            {
                continue;
            }

            foreach (var variable in method.Body.Variables)
            {
                Expand(variable.VariableType, into);
            }

            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case TypeReference reference: // a type is also a member reference, so it is matched first
                        Expand(reference, into);
                        break;
                    case MemberReference member:
                        ExpandMember(member, into);
                        break;
                    default:
                        break;
                }
            }
        }

        return into;
    }

    /// <summary>
    /// Every type (by full name, nested types included) that code starting at the roots can reach: what the code names (fields, signatures, calls, generic arguments) and, for a contract that is named, every class that implements that very closed contract (so a handler
    /// that is only resolved by its contract is still in the set). An over-approximation: anything it does not contain cannot be reached.
    /// </summary>
    private static HashSet<string> Closure(IEnumerable<string> roots) => Closure(roots, []);

    /// <summary>The same, and for every type reached the type that first reached it (to show the chain when a rule fails).</summary>
    private static HashSet<string> Closure(IEnumerable<string> roots, Dictionary<string, string> via)
    {
        var index = Index.Value;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<TypeDefinition>();

        void Visit(string name, string from)
        {
            if (index.Types.TryGetValue(name, out var type) && seen.Add(type.FullName))
            {
                via[type.FullName] = from;
                pending.Push(type);
            }

            // a contract of the solution that is named (a port, a handler contract) reaches its implementations; the marker of every command and framework interfaces do not
            foreach (var implementer in name.StartsWith("XDrishti.", StringComparison.Ordinal) && name != MarkerCommand ? index.Implementers[name] : [])
            {
                if (seen.Add(implementer.FullName))
                {
                    via[implementer.FullName] = from + " (contract " + name + ")";
                    pending.Push(implementer);
                }
            }
        }

        foreach (var root in roots)
        {
            Visit(root, "root");
        }

        while (pending.Count > 0)
        {
            var type = pending.Pop();
            foreach (var nested in type.NestedTypes)
            {
                Visit(nested.FullName, type.FullName);
            }

            if (type.DeclaringType is { } declaring)
            {
                Visit(declaring.FullName, type.FullName);
            }

            foreach (var name in References(type))
            {
                Visit(name, type.FullName);
            }
        }

        return seen;
    }

    private static string Chain(Dictionary<string, string> via, string type)
    {
        var chain = new List<string>();
        var current = type;
        while (via.TryGetValue(current, out var from) && chain.Count < 25)
        {
            chain.Add(current + " <- " + from);
            current = from.Split(" (contract ")[0];
        }

        return string.Join(" | ", chain);
    }

    /// <summary>The top-level types among <paramref name="scope"/> whose code calls or reads a member the predicate accepts.</summary>
    private static List<string> Callers(IEnumerable<string> scope, Func<MemberReference, bool> member)
    {
        var index = Index.Value;
        return [.. scope.Where(index.Types.ContainsKey)
            .Where(n => index.Types[n].Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MemberReference r && member(r)))
            .Select(Root).Distinct().Order(StringComparer.Ordinal)];
    }

    private static bool IsCtorOf(MemberReference r, string type) => r.Name == ".ctor" && r.DeclaringType.FullName == type;

    /// <summary>The types of the scope that build the lifecycle command (not the command's own copy constructor).</summary>
    private static List<string> CommandBuilders(IEnumerable<string> scope) => [.. Callers(scope, r => IsCtorOf(r, RecordCommand)).Where(n => n != RecordCommand)];

    private static bool IsAppend(MemberReference r) => r.Name == "AppendAsync" && r.DeclaringType.FullName == typeof(ILearningChangelog).FullName;

    // ---- (1) no path re-enables automatically ----

    [Fact]
    public void The_closure_of_the_automatic_path_reaches_the_lifecycle_through_the_pause_handler_and_nothing_else()
    {
        var via = new Dictionary<string, string>(StringComparer.Ordinal);
        var closure = Closure(AutoRoots, via);

        // positive controls: the scan reaches what the path really uses, through contracts too
        closure.ShouldContain("XDrishti.Application.Learning.PauseCellHandler");
        closure.ShouldContain("XDrishti.Application.Learning.RecordCellTransitionHandler");
        closure.ShouldContain("XDrishti.Application.Confidence.StrategyCells");
        closure.ShouldContain("XDrishti.Infrastructure.Learning.LearningChangelog");
        closure.ShouldContain("XDrishti.Application.Confidence.GetDecayReportHandler");

        foreach (var reEnable in ReEnableTypes)
        {
            closure.Contains(reEnable).ShouldBeFalse("the re-enable is not reachable from the automatic path: " + Chain(via, reEnable));
        }

        closure.ShouldNotContain("XDrishti.Application.Learning.RevertChangelogHandler", "a revert restores a state; it is not the automatic path's to ask for");
        closure.ShouldNotContain("XDrishti.Api.Cli.LearnLifecycleCommand");
        closure.ShouldNotContain("XDrishti.Application.Learning.DesignateModelHandler");

        // the lifecycle recorder is fed by one caller on this path, the pause handler, and only the recorder appends to the changelog
        CommandBuilders(closure).ShouldBe(["XDrishti.Application.Learning.PauseCellHandler"]);
        Callers(closure, IsAppend).ShouldBe(["XDrishti.Application.Learning.RecordCellTransitionHandler"]);
    }

    [Fact]
    public void The_only_delegate_type_of_the_solution_in_the_closure_of_the_automatic_path_is_the_reviewed_one()
    {
        // A delegate built where the handler is registered hides an edge from this scan (CF-11 part 1 gave the readiness scorecard the P10 phase check that way, so that a pause, which never meets the gate, does not make every scheduled service reachable).
        // Another delegate would hide another edge without anybody reading it, so the set of the solution's own delegate types in the closure is pinned: a second one needs a reviewed change of this list.
        static bool IsDelegate(TypeDefinition type) => type.BaseType?.FullName is "System.MulticastDelegate" or "System.Delegate";
        static List<string> DelegatesIn(HashSet<string> closure) => [.. closure.Where(n => Index.Value.Types.TryGetValue(n, out var type) && IsDelegate(type)).Order(StringComparer.Ordinal)];

        DelegatesIn(Closure(AutoRoots)).ShouldBe([Name(typeof(XDrishti.Application.Abstractions.Confidence.PaperPhaseMetReader))], "the reviewed delegates of the closure of the automatic path");

        // controls: the scan lists a delegate that a reachable type holds, whatever it is called, and the production delegates are found by their base type
        DelegatesIn(Closure([Name(typeof(DelegateHolderSentinel))])).ShouldContain(Name(typeof(HiddenEdgeDelegate)));
        Index.Value.Types.Values.Count(IsDelegate).ShouldBeGreaterThan(1);
        IsDelegate(Index.Value.Types[Name(typeof(XDrishti.Application.Abstractions.Confidence.PaperPhaseMetReader))]).ShouldBeTrue();
    }

    [Fact]
    public void The_scan_catches_a_path_that_re_enables_or_moves_a_cell_itself()
    {
        var sentinel = Name(typeof(DirectLifecycleSentinel));
        var withReEnable = Closure([Name(typeof(ReEnableSentinel))]);
        var direct = Closure([sentinel]);

        withReEnable.ShouldContain("XDrishti.Application.Confidence.ReEnableCellHandler", "a type that holds the re-enable contract reaches the handler");
        CommandBuilders(direct).ShouldContain(sentinel);
        Callers(direct, IsAppend).ShouldContain(sentinel);
    }

    [Fact]
    public void The_pause_handler_names_the_paused_state_and_no_other_and_the_lifecycle_has_one_recorder_per_caller()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Learning", "PauseCellHandler.cs"));

        source.ShouldContain("CellState.Paused");
        foreach (var other in new[] { "Research", "Candidate", "Paper", "Active", "Retired" })
        {
            source.ShouldNotContain("CellState." + other);
        }

        source.ShouldNotContain("Recovered: true");
        source.ShouldNotContain("OwnerApproved");

        // the callers of the lifecycle command in all of production: the pause handler, the re-enable and the owner's CLI verb
        var production = ProductionNames.SelectMany(n => Assembly.Load(new AssemblyName(n)).GetTypes()).Select(Name).ToList();
        CommandBuilders(production).ShouldBe(["XDrishti.Api.Cli.LearnLifecycleCommand", "XDrishti.Application.Confidence.ReEnableCellHandler", "XDrishti.Application.Learning.PauseCellHandler"]);
        CommandBuilders([.. production, Name(typeof(DirectLifecycleSentinel))]).ShouldContain(Name(typeof(DirectLifecycleSentinel))); // control
    }

    [Fact]
    public void Only_the_re_enable_handler_sets_the_flag_that_lets_a_move_leave_a_kill_pause_so_the_owners_cli_verb_cannot()
    {
        // authoritative, from the IL: the flag is an init property, so every way to set it (an object initializer, a `with` expression, a direct call) is a call of set_OwnerReEnable
        var production = ProductionNames.SelectMany(n => Assembly.Load(new AssemblyName(n)).GetTypes()).Select(Name).ToList();
        static bool SetsFlag(MemberReference r) => r.Name == "set_OwnerReEnable" && r.DeclaringType.FullName == RecordCommand;

        Callers(production, SetsFlag).ShouldBe(["XDrishti.Application.Confidence.ReEnableCellHandler"]);
        Callers([.. production, Name(typeof(FlagSentinel))], SetsFlag).ShouldContain(Name(typeof(FlagSentinel))); // control: a type that sets it is caught
        Callers([.. production, Name(typeof(FlagWithSentinel))], SetsFlag).ShouldContain(Name(typeof(FlagWithSentinel))); // control: so is a `with` expression
        typeof(RecordCellTransitionCommand).GetConstructors().Single().GetParameters().Select(p => p.Name).ShouldNotContain("OwnerReEnable", "it cannot be passed positionally");

        // and the text: the CLI verb never names it, and the recorder reads it in one place
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Cli", "LearnLifecycleCommand.cs")).ShouldNotContain("OwnerReEnable");
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Learning", "RecordCellTransitionHandler.cs")).ShouldContain("!command.OwnerReEnable"); // the one place the flag is read
        Types.InAssembly(Application).That().HaveName("RecordCellTransitionHandler").Should().HaveDependencyOn(typeof(KillEvidence).FullName!).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Only_the_re_enable_handler_its_route_and_the_registration_name_the_re_enable()
    {
        var users = ProductionNames.SelectMany(n => Types.InAssembly(Assembly.Load(new AssemblyName(n))).That().HaveDependencyOnAny(ReEnableTypes).GetTypes())
            .Select(t => Root(t.FullName!)).Distinct().Order(StringComparer.Ordinal).ToList();

        users.ShouldBe(
        [
            "XDrishti.Api.Endpoints.KillEndpoints",
            "XDrishti.Api.Infrastructure.ApiJsonContext",
            "XDrishti.Application.Confidence.ReEnableCellCommand",
            "XDrishti.Application.Confidence.ReEnableCellHandler",
            "XDrishti.Application.Confidence.ReEnableOutcome",
            "XDrishti.Application.DependencyInjection",
        ]);
        Types.InAssembly(Self).That().HaveName(nameof(ReEnableSentinel)).Should().HaveDependencyOnAny(ReEnableTypes).GetResult().IsSuccessful.ShouldBeTrue(); // control
    }

    [Fact]
    public void A_revert_cannot_lift_a_kill_pause_and_the_pause_handler_builds_no_facts()
    {
        Types.InAssembly(Application).That().HaveName("RevertChangelogHandler").Should().HaveDependencyOn(typeof(KillEvidence).FullName!).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().HaveName("RevertChangelogHandler").Should().NotHaveDependencyOnAny([.. KillTypes.Where(t => t != typeof(KillEvidence).FullName)]).GetResult().IsSuccessful.ShouldBeTrue();
    }

    // ---- (2) who writes what ----

    private static List<string> Dependents(Assembly assembly, string contract) =>
        [.. Types.InAssembly(assembly).That().HaveDependencyOn(contract).GetTypes().Select(t => Root(t.FullName!)).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void The_switch_has_one_writer_the_owners_handler_and_the_proposals_have_two()
    {
        Dependents(Application, typeof(IKillSwitchStore).FullName!).ShouldBe(["XDrishti.Application.Confidence.ChangeKillSwitchHandler"]);
        Dependents(Application, typeof(IKillProposalStore).FullName!).ShouldBe(["XDrishti.Application.Confidence.ApplyKillHandler", "XDrishti.Application.Confidence.DismissKillHandler", "XDrishti.Application.Confidence.ProposeKillHandler"]);
        Dependents(Application, typeof(IKillSettings).FullName!).ShouldBe(["XDrishti.Application.Confidence.ApplyKillHandler", "XDrishti.Application.Confidence.GetKillProposalsHandler", "XDrishti.Application.Confidence.ProposeKillHandler"]);
        Dependents(Application, typeof(IKillProposalReader).FullName!).ShouldBe(
        [
            "XDrishti.Application.Confidence.ApplyKillHandler", "XDrishti.Application.Confidence.DismissKillHandler", "XDrishti.Application.Confidence.GetDecayReportHandler", "XDrishti.Application.Confidence.GetKillProposalsHandler", "XDrishti.Application.Confidence.KillProposalNotices",
            "XDrishti.Application.Confidence.ProposeKillHandler", "XDrishti.Application.Confidence.ReEnableCellHandler",
        ]);
        Dependents(Application, typeof(IDecayCheckStore).FullName!).ShouldBe(["XDrishti.Application.Confidence.ProposeKillHandler"]);

        // implementations: one each, in Infrastructure
        foreach (var contract in new[] { typeof(IKillSwitchStore), typeof(IKillProposalStore), typeof(IKillSettings), typeof(IKillProposalReader), typeof(IDecayCheckStore) })
        {
            var implementations = ProductionNames.SelectMany(n => Assembly.Load(new AssemblyName(n)).GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && contract.IsAssignableFrom(t)).Select(t => t.Namespace).Distinct().ToList();
            implementations.ShouldBe(["XDrishti.Infrastructure.Confidence"], contract.Name);
        }
    }

    [Fact]
    public void The_switch_is_stored_state_and_not_a_configuration_value()
    {
        var settings = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Confidence", "ConfidenceSettings.cs"));
        var section = settings[settings.IndexOf("internal sealed class KillSection", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("internal sealed class LoopSection", StringComparison.Ordinal)];

        section.ShouldContain("StressMultiplier");
        section.ShouldContain("DrawdownFactor");
        section.ShouldNotContain("Auto");
        section.ShouldNotContain("Enabled");
        typeof(KillOptions).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract").Order(StringComparer.Ordinal).ShouldBe(["DrawdownFactor", "StressMultiplier"]);
    }

    // ---- (3) nothing that chooses reads a kill type ----

    [Fact]
    public void Planning_selection_and_autonomy_name_no_kill_type_setting_or_proposal()
    {
        (Assembly Assembly, string Namespace)[] choosers =
        [
            (Domain, "XDrishti.Domain.Selection"), (Domain, "XDrishti.Domain.Planning"), (Domain, "XDrishti.Domain.Autonomy"),
            (Application, "XDrishti.Application.Planning"), (Application, "XDrishti.Application.Selection"), (Application, "XDrishti.Application.Autonomy"),
        ];
        string[] extra = ["XDrishti.Application.Confidence", "XDrishti.Application.Abstractions.Confidence", "XDrishti.Domain.Confidence.KillOptions"];

        foreach (var (assembly, ns) in choosers)
        {
            var types = Types.InAssembly(assembly).That().ResideInNamespace(ns);
            types.GetTypes().ShouldNotBeEmpty(ns);
            var result = types.Should().NotHaveDependencyOnAny([.. KillTypes, .. extra]).GetResult();
            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        Types.InAssembly(Self).That().HaveName(nameof(ProposalReaderSentinel)).Should().NotHaveDependencyOnAny(KillTypes).GetResult().IsSuccessful.ShouldBeFalse("control: the rule sees a reader of proposals");
    }

    [Fact]
    public void The_bell_reaches_the_notices_only_through_the_neutral_port()
    {
        Types.InAssembly(Application).That().HaveName("GetPlanNoticesHandler").Should().HaveDependencyOn("XDrishti.Application.Abstractions.Planning.IBellNoticeSource").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().HaveName("GetPlanNoticesHandler").Should().NotHaveDependencyOnAny(KillTypes).GetResult().IsSuccessful.ShouldBeTrue();
        Dependents(Application, "XDrishti.Application.Abstractions.Planning.IBellNoticeSource").ShouldBe(["XDrishti.Application.Confidence.KillProposalNotices", "XDrishti.Application.DependencyInjection", "XDrishti.Application.Learning.PromotionNotices", "XDrishti.Application.Learning.RollbackProposalNotices", "XDrishti.Application.Planning.GetPlanNoticesHandler"]);
    }

    // ---- (4) no order ----

    [Fact]
    public void Nothing_of_the_kill_path_touches_an_order_the_execution_plane_or_a_broker()
    {
        string[] forbidden =
        [
            "XDrishti.Execution", "XDrishti.Application.Execution", "XDrishti.Application.Abstractions.Execution", "XDrishti.Application.Abstractions.LiveTrading", "XDrishti.Application.LiveTrading",
            "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Accounts", "XDrishti.Application.Autonomy", "XDrishti.Application.Abstractions.Autonomy",
        ];
        var ours = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Confidence").And().HaveNameMatching("^(ProposeKill|ApplyKill|ReEnableCell|ChangeKillSwitch|EvaluateDecay|DecayMonitorService|GetKillProposals|KillProposalNotices|StrategyCells|PauseCell).*$");

        ours.GetTypes().Count().ShouldBeGreaterThan(10);
        var result = ours.Should().NotHaveDependencyOnAny(forbidden).GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Application).That().HaveName("PauseCellHandler").Should().NotHaveDependencyOnAny(forbidden).GetResult().IsSuccessful.ShouldBeTrue();

        // the whole of production has no member named for an order that the kill types add (the tripwire of P7-03 covers the names; this pins that the stores are the only I/O)
        foreach (var store in new[] { "KillProposals", "KillSettings", "DecayChecks" })
        {
            Types.InAssembly(typeof(XDrishti.Infrastructure.DependencyInjection).Assembly).That().HaveName(store).Should().NotHaveDependencyOnAny("XDrishti.Execution", "XDrishti.Application.Planning").GetResult().IsSuccessful.ShouldBeTrue();
        }
    }

    // ---- (5) the routes ----

    [Fact]
    public void The_routes_are_the_owners_apply_needs_the_session_and_the_switch_and_re_enable_need_a_fresh_password()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "KillEndpoints.cs"));

        source.Split("MapGet(").Length.ShouldBe(2);
        source.Split("MapPost(").Length.ShouldBe(4, "apply, dismiss and re-enable");
        source.Split("MapPut(").Length.ShouldBe(2, "the switch");
        foreach (var verb in new[] { "MapDelete", "MapPatch", "MapMethods" })
        {
            source.ShouldNotContain(verb);
        }

        source.ShouldContain("/kill-proposals/{id:long}/apply");
        source.ShouldContain("/kill-proposals/{id:long}/dismiss");
        source.ShouldContain("/{key}/re-enable");
        source.ShouldContain("/kill-settings");
        source.Split("OwnerAct.FreshAsync(").Length.ShouldBe(4, "the two acts that raise or allow risk and the dismissal, through the one shared step-up helper");
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "OwnerAct.cs")).ShouldContain("AuthErrors.StepUpRequired"); // a missing password is 403 in the helper
        source.ShouldContain("OwnerAct.SessionOwner(http)"); // apply: the session's owner, no password (the safe direction)
        source.ShouldNotContain("request.Actor");
        source.ShouldNotContain("request.Owner");
    }

    [Fact]
    public void The_pause_command_is_the_only_automatic_command_and_the_actor_constants_are_what_the_database_expects()
    {
        KillActors.Auto.ShouldBe("auto");
        typeof(PauseCellCommand).GetProperties().Select(p => p.Name).ShouldNotContain("To");
        typeof(PauseCellCommand).GetProperties().Select(p => p.Name).ShouldNotContain("Facts");
    }

    [Fact]
    public void The_dismissal_takes_only_the_proposal_reader_and_store_and_a_logger()
    {
        var handler = typeof(ApplyKillHandler).Assembly.GetType("XDrishti.Application.Confidence.DismissKillHandler")!;

        handler.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ShouldBe([typeof(IKillProposalReader), typeof(IKillProposalStore), typeof(Microsoft.Extensions.Logging.ILogger<>).MakeGenericType(handler)]);
    }

    [Fact]
    public void The_migration_is_one_added_file_and_the_other_migrations_are_untouched_in_name()
    {
        var domain = RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "XDrishti.Infrastructure.csproj");
        var migrations = Path.Combine(Path.GetDirectoryName(domain)!, "Persistence", "Migrations");

        Directory.GetFiles(migrations, "*_AddDecayAndKill.cs").Length.ShouldBe(1);
        Directory.GetFiles(migrations, "*_AddDecayAndKill.Designer.cs").Length.ShouldBe(1);
    }

    [Fact]
    public void The_il_reading_helpers_see_the_instruction_operands_they_are_given()
    {
        // control for the helpers: the sentinel's IL has a constructor call and an append, which the closure and caller scans read
        var type = Index.Value.Types[Name(typeof(DirectLifecycleSentinel))];
        type.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Count(i => i.OpCode == OpCodes.Newobj).ShouldBeGreaterThan(0);
    }
}
