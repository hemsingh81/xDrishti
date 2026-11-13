using System.Reflection;
using Mono.Cecil;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec CF-10 part 10d-1, D7 to D9 (ADR 0220): promotion, rollback and the ML switches. A rollback lowers reliance on ML and may run automatically behind a setting (default OFF); a promotion raises it and, automatic or not, needs the rule. Enforced here, each scan with a sentinel
/// it must catch: (1) the closure of everything the automatic rollback can reach contains the registry only through the demotion handler, which names the role Challenger and no other, and reaches no promotion, no re-promotion and no switch writer; (2) the switch has one writer (the
/// owner's handler), the proposals two, and each contract one implementation, in Infrastructure; (3) the promotion command is built only by the daily run (as <c>auto</c>) and by the owner's route; (4) Planning, Selection and Autonomy name none of it; (5) none of it touches an order or the
/// execution plane; (6) the rollback trigger is the P6-09 detector itself and not a copy of its numbers; (7) the routes are the owner's, with a fresh password where reliance on ML may rise.
/// </summary>
public sealed class MlRollbackArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Self = typeof(MlRollbackArchitectureTests).Assembly;

    private static readonly string[] ProductionNames =
    [
        "XDrishti.Api", "XDrishti.Application", "XDrishti.Domain", "XDrishti.Execution", "XDrishti.Feed", "XDrishti.Hosting", "XDrishti.Indicators", "XDrishti.Infrastructure", "XDrishti.Learning.MlNet", "XDrishti.Mcp",
        "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Worker",
    ];

    // where the automatic rollback starts: the evaluation of one champion and the application of a proposal (the daily run only calls the first)
    private static readonly string[] AutoRollbackRoots = ["XDrishti.Application.Learning.ProposeRollbackHandler", "XDrishti.Application.Learning.ApplyRollbackHandler"];

    // what the rollback path must never reach: a promotion or re-promotion, a change of a switch, the owner's CLI designation, a revert and the cell re-enable
    private static readonly string[] NotForRollback =
    [
        "XDrishti.Application.Learning.PromoteChallengerHandler", "XDrishti.Application.Learning.PromoteChallengerCommand", "XDrishti.Application.Learning.PromotionOutcome",
        "XDrishti.Application.Learning.ChangeMlSettingsHandler", "XDrishti.Application.Learning.ChangeMlSettingsCommand", "XDrishti.Application.Learning.EvaluateMlModelsHandler",
        "XDrishti.Api.Cli.LearnDesignateCommand", "XDrishti.Application.Learning.OwnerDesignateModelHandler", "XDrishti.Application.Learning.OwnerDesignateModelCommand",
        "XDrishti.Application.Learning.RevertChangelogHandler", "XDrishti.Application.Confidence.ReEnableCellHandler",
    ];

    private const string DesignateCommand = "XDrishti.Application.Learning.DesignateModelCommand";

    private const string PromoteCommand = "XDrishti.Application.Learning.PromoteChallengerCommand";

    private const string OwnerDesignateCommand = "XDrishti.Application.Learning.OwnerDesignateModelCommand";

    private static readonly string[] OwnerDesignateSetters = ["set_ModelId", "set_Role", "set_Reason", "set_Actor"];

    // the init setters through which an object initializer or a `with` expression changes a field of the two commands that name a champion
    private static readonly string[] DesignateSetters = ["set_ModelId", "set_Role", "set_Reason", "set_Actor", "set_Evidence", "set_ExpectedCurrentRole"];

    private static readonly string[] PromoteSetters = ["set_ModelId", "set_Actor", "set_Reason"];

    private const string MarkerCommand = "XDrishti.Application.Abstractions.Messaging.ICommand";

    // every type of this slice that a chooser (Planning, Selection, Autonomy) must not name
    private static readonly string[] SliceTypes =
    [
        "XDrishti.Application.Learning.PromoteChallengerHandler", "XDrishti.Application.Learning.PromoteChallengerCommand", "XDrishti.Application.Learning.DemoteModelHandler", "XDrishti.Application.Learning.DemoteModelCommand",
        "XDrishti.Application.Learning.ProposeRollbackHandler", "XDrishti.Application.Learning.ProposeRollbackCommand", "XDrishti.Application.Learning.ApplyRollbackHandler", "XDrishti.Application.Learning.ApplyRollbackCommand",
        "XDrishti.Application.Learning.ChangeMlSettingsHandler", "XDrishti.Application.Learning.ChangeMlSettingsCommand", "XDrishti.Application.Learning.EvaluateMlModelsHandler", "XDrishti.Application.Learning.MlMonitorService",
        "XDrishti.Application.Learning.GetRollbackProposalsHandler", "XDrishti.Application.Learning.RollbackProposalNotices", "XDrishti.Application.Learning.NoRollbackEvidence", "XDrishti.Application.Learning.NoChallengerEvidence",
        "XDrishti.Application.Learning.MlActors", "XDrishti.Application.Learning.MlSafetyErrors", "XDrishti.Application.Learning.PromotionNotices",
        "XDrishti.Application.Learning.OwnerDesignateModelHandler", "XDrishti.Application.Learning.OwnerDesignateModelCommand",
        typeof(IRollbackProposalReader).FullName!, typeof(IRollbackProposalStore).FullName!, typeof(IMlSwitchSettings).FullName!, typeof(IMlSwitchStore).FullName!, typeof(IMlMonitorChecks).FullName!,
        typeof(IRollbackEvidence).FullName!, typeof(IChallengerEvidence).FullName!, typeof(MlSwitches).FullName!,
        typeof(MlPromotion).FullName!, typeof(RollbackPolicy).FullName!, typeof(MlSwitch).FullName!, typeof(RegimeCellRule).FullName!,
    ];

    /// <summary>Stands for an automatic rollback that promotes: it holds the promotion handler's contract. The closure of the scan must reach the handler through it.</summary>
    internal sealed class PromotionSentinel(ICommandHandler<PromoteChallengerCommand, PromotionOutcome> promote)
    {
        public ICommandHandler<PromoteChallengerCommand, PromotionOutcome> Handler => promote;
    }

    /// <summary>Stands for a path that names a champion itself: it builds the designation command with the champion role and calls the registry.</summary>
    internal sealed class DirectDesignationSentinel(IModelRegistry registry)
    {
        public ValueTask<bool> Raise(long id) => registry.DesignateAsync(id, ModelRole.Champion, "why", "auto", DateTimeOffset.UnixEpoch, [], null, CancellationToken.None);

        public static DesignateModelCommand Command(long id) => new(id, ModelRole.Champion, "why", "auto");
    }

    /// <summary>Stands for a path that writes a switch.</summary>
    internal sealed class SwitchWriterSentinel(IMlSwitchStore store)
    {
        public ValueTask<long> Raise() => store.AppendAsync("ml.influence.enabled", true, "owner:x", "why", CancellationToken.None);
    }

    /// <summary>Stands for a path that builds the owner's command-line designation (a constructor call, then a <c>with</c> expression).</summary>
    internal static class OwnerDesignationSentinel
    {
        public static OwnerDesignateModelCommand Command() => new(1, ModelRole.Champion, "why", "auto");

        public static OwnerDesignateModelCommand Again(OwnerDesignateModelCommand command) => command with { Actor = "auto" };
    }

    /// <summary>Stands for a path that builds the promotion command.</summary>
    internal static class PromotionBuilderSentinel
    {
        public static PromoteChallengerCommand Command() => new(1, "auto", "why");
    }

    /// <summary>Stands for a path that turns a command into a champion designation with a <c>with</c> expression (no constructor call).</summary>
    internal static class DesignationWithSentinel
    {
        public static DesignateModelCommand Command(DesignateModelCommand command) => command with { Role = ModelRole.Champion };
    }

    /// <summary>Stands for a path that changes who promotes with a <c>with</c> expression (no constructor call).</summary>
    internal static class PromotionWithSentinel
    {
        public static PromoteChallengerCommand Command(PromoteChallengerCommand command) => command with { Actor = "auto" };
    }

    /// <summary>Stands for a chooser that reads the rollback proposals.</summary>
    internal sealed class ProposalReaderSentinel(IRollbackProposalReader proposals)
    {
        public ValueTask<RollbackProposalRow?> Read() => proposals.FindAsync(1, CancellationToken.None);
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
                // of the test assembly only this class's own sentinels are indexed: another class's sentinels implement the same contracts (the influence switch, for one) and would lead the closure out of production through the test classes
                if (path != Self.Location || type.FullName.StartsWith(typeof(MlRollbackArchitectureTests).FullName + "/", StringComparison.Ordinal))
                {
                    types[type.FullName] = type;
                }
            }
        }

        var implementers = types.Values.SelectMany(t => t.Interfaces.Select(i => (Interface: i.InterfaceType.FullName, Type: t))).ToLookup(x => x.Interface, x => x.Type, StringComparer.Ordinal);
        return new TypeIndex(types, implementers);
    });

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
                    case TypeReference reference:
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

    /// <summary>Every type that code starting at the roots can reach: what the code names and, for a contract of the solution that is named, every class that implements that very closed contract. An over-approximation: anything it does not contain cannot be reached.</summary>
    private static HashSet<string> Closure(IEnumerable<string> roots, Dictionary<string, string>? via = null)
    {
        via ??= [];
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

    private static List<string> Callers(IEnumerable<string> scope, Func<MemberReference, bool> member)
    {
        var index = Index.Value;
        return [.. scope.Where(index.Types.ContainsKey)
            .Where(n => index.Types[n].Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MemberReference r && member(r)))
            .Select(Root).Distinct().Order(StringComparer.Ordinal)];
    }

    private static bool IsCtorOf(MemberReference r, string type) => r.Name == ".ctor" && r.DeclaringType?.FullName == type;

    /// <summary>The types that make a new command: a constructor call, a <c>with</c> expression (the record's clone method <c>&lt;Clone&gt;$</c>) or an init setter of a property (<c>set_X</c>), which is how an object initializer or a <c>with</c> changes a field. The command's own type is not counted.</summary>
    private static List<string> Builders(IEnumerable<string> scope, string command, params string[] setters) =>
        [.. Callers(scope, r => IsCtorOf(r, command) || (r.DeclaringType?.FullName == command && (r.Name == "<Clone>$" || setters.Contains(r.Name, StringComparer.Ordinal)))).Where(n => n != command)];

    private static bool IsDesignate(MemberReference r) => r.Name == "DesignateAsync" && r.DeclaringType?.FullName == typeof(IModelRegistry).FullName;

    private static bool IsSwitchAppend(MemberReference r) => r.Name == "AppendAsync" && r.DeclaringType.FullName == typeof(IMlSwitchStore).FullName;

    private static List<string> Production() => [.. ProductionNames.SelectMany(n => Assembly.Load(new AssemblyName(n)).GetTypes()).Select(Name)];

    private static List<string> Dependents(Assembly assembly, string contract) =>
        [.. Types.InAssembly(assembly).That().HaveDependencyOn(contract).GetTypes().Select(t => Root(t.FullName!)).Distinct().Order(StringComparer.Ordinal)];

    // ---- (1) the automatic rollback only demotes ----

    [Fact]
    public void The_closure_of_the_automatic_rollback_reaches_the_registry_through_the_demotion_handler_and_no_promotion_or_switch_writer()
    {
        var via = new Dictionary<string, string>(StringComparer.Ordinal);
        var closure = Closure(AutoRollbackRoots, via);

        // positive controls: the scan reaches what the path really uses, through contracts too
        closure.ShouldContain("XDrishti.Application.Learning.DemoteModelHandler");
        closure.ShouldContain("XDrishti.Application.Learning.DesignateModelHandler");
        closure.ShouldContain("XDrishti.Infrastructure.Learning.ModelRegistry");
        closure.ShouldContain("XDrishti.Application.Learning.MlInfluenceEvaluator");
        closure.ShouldContain("XDrishti.Domain.Learning.RollbackPolicy");
        closure.ShouldContain("XDrishti.Domain.Learning.CalibrationDriftDetector");
        closure.ShouldContain("XDrishti.Infrastructure.Learning.RollbackProposals");

        foreach (var forbidden in NotForRollback)
        {
            closure.Contains(forbidden).ShouldBeFalse("not reachable from the automatic rollback: " + Chain(via, forbidden));
        }

        // the registry's designation is asked for by one handler, the designation command is built by one (the demotion), and nothing in the closure writes a switch
        Callers(closure, IsDesignate).ShouldBe(["XDrishti.Application.Learning.DesignateModelHandler"]);
        Builders(closure, DesignateCommand, DesignateSetters).ShouldBe(["XDrishti.Application.Learning.DemoteModelHandler"]);
        Callers(closure, IsSwitchAppend).ShouldBeEmpty();
    }

    [Fact]
    public void The_scan_catches_a_rollback_that_promotes_names_a_champion_itself_or_writes_a_switch()
    {
        var promotes = Closure([Name(typeof(PromotionSentinel))]);
        var designates = Closure([Name(typeof(DirectDesignationSentinel))]);
        var writes = Closure([Name(typeof(SwitchWriterSentinel))]);

        promotes.ShouldContain("XDrishti.Application.Learning.PromoteChallengerHandler", "a type that holds the promotion contract reaches the handler");
        Builders(designates, DesignateCommand, DesignateSetters).ShouldContain(Name(typeof(DirectDesignationSentinel)));
        Callers(designates, IsDesignate).ShouldContain(Name(typeof(DirectDesignationSentinel)));
        Callers(writes, IsSwitchAppend).ShouldContain(Name(typeof(SwitchWriterSentinel)));
    }

    [Fact]
    public void The_builder_scans_also_see_a_with_expression_and_an_init_setter_not_only_a_constructor_call()
    {
        var designationWith = Name(typeof(DesignationWithSentinel));
        var promotionWith = Name(typeof(PromotionWithSentinel));

        // controls: the sentinels build no command with a constructor, so only the clone method / setter detection can see them
        Callers([designationWith], r => IsCtorOf(r, DesignateCommand)).ShouldBeEmpty();
        Callers([promotionWith], r => IsCtorOf(r, PromoteCommand)).ShouldBeEmpty();
        Builders([designationWith], DesignateCommand, DesignateSetters).ShouldBe([designationWith]);
        Builders([promotionWith], PromoteCommand, PromoteSetters).ShouldBe([promotionWith]);

        // and the whole of production changes neither command with a `with` expression or a setter: no such caller exists
        Builders(Production(), DesignateCommand, DesignateSetters).ShouldNotContain(n => n.EndsWith("Sentinel", StringComparison.Ordinal));
        Callers(Production(), r => r.DeclaringType?.FullName == DesignateCommand && (r.Name == "<Clone>$" || DesignateSetters.Contains(r.Name))).Where(n => n != DesignateCommand).ShouldBeEmpty();
        Callers(Production(), r => r.DeclaringType?.FullName == PromoteCommand && (r.Name == "<Clone>$" || PromoteSetters.Contains(r.Name))).Where(n => n != PromoteCommand).ShouldBeEmpty();
    }

    [Fact]
    public void The_command_line_designation_bypasses_the_promotion_rule_until_a_source_of_evidence_exists_and_the_day_one_does_this_test_fails()
    {
        // OWNER DECISION NEEDED (ADR 0220): `xd learn designate ... champion` names a champion on the champion rule alone (the evidence of D7 has no source yet, and
        // otherwise no champion could be named at all). That is acceptable only while the promotion rule can never be satisfied. The day a real source of challenger or
        // predicate evidence exists, the verb has to go through PromoteChallengerHandler first.
        const string Message = "A production source of challenger or influence evidence exists, and the command line can still name a champion without the promotion rule: the designate path (OwnerDesignateModelHandler / xd learn designate) has to go through PromoteChallengerHandler first. Owner decision: see ADR 0220.";
        var production = ProductionNames.Select(n => Assembly.Load(new AssemblyName(n))).ToList();

        List<string> Sources(IEnumerable<Assembly> assemblies) =>
            [.. assemblies.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && (typeof(IChallengerEvidence).IsAssignableFrom(t) || typeof(IMlInfluenceEvidence).IsAssignableFrom(t)))
                .Select(t => t.FullName!).Where(n => n is not ("XDrishti.Application.Learning.NoChallengerEvidence" or "XDrishti.Application.Learning.NoMlInfluenceEvidence")).Order(StringComparer.Ordinal)];

        // the verb can name a champion without the promotion rule while the command line's use case does not call the promotion handler
        var cliGoesThroughPromotion = Types.InAssembly(Application).That().HaveName("OwnerDesignateModelHandler").Should().HaveDependencyOn(PromoteCommand).GetResult().IsSuccessful;
        var cliReachesTheOwnersHandler = Types.InAssembly(typeof(Program).Assembly).That().HaveName("LearnDesignateCommand").Should().HaveDependencyOn("XDrishti.Application.Learning.OwnerDesignateModelCommand").GetResult().IsSuccessful;

        cliReachesTheOwnersHandler.ShouldBeTrue("the verb is expected to use the owner's designation use case");
        if (!cliGoesThroughPromotion)
        {
            Sources(production).ShouldBeEmpty(Message);
        }

        // control: the same scan sees a real source, and would fail the test above with the message
        var withSentinels = Sources([.. production, Self]);
        withSentinels.ShouldContain(Name(typeof(ChallengerEvidenceSentinel)).Replace('/', '+'));
        withSentinels.ShouldContain(Name(typeof(InfluenceEvidenceSentinel)).Replace('/', '+'));
        Should.Throw<Shouldly.ShouldAssertException>(() => withSentinels.ShouldBeEmpty(Message)).Message.ShouldContain("has to go through PromoteChallengerHandler first");
    }

    [Fact]
    public void The_demotion_handler_names_the_challenger_role_and_no_other_and_has_no_target_in_its_command()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Learning", "DemoteModelHandler.cs"));

        // the champion role appears once, as the role the model must have NOW (the registry checks it under its lock), never as a target
        const string Expected = "ExpectedCurrentRole: ModelRole.Champion";
        source.ShouldContain("ModelRole.Challenger");
        source.ShouldContain(Expected);
        var withoutExpected = source.Replace(Expected, string.Empty, StringComparison.Ordinal);
        foreach (var other in new[] { "Champion", "Retired" })
        {
            withoutExpected.ShouldNotContain("ModelRole." + other);
        }

        typeof(DemoteModelCommand).GetProperties().Select(p => p.Name).ShouldNotContain("Role");
        Builders(Production(), DesignateCommand, DesignateSetters).Order(StringComparer.Ordinal).ShouldBe(
        [
            "XDrishti.Application.Learning.DemoteModelHandler", "XDrishti.Application.Learning.OwnerDesignateModelHandler", "XDrishti.Application.Learning.PromoteChallengerHandler",
        ]);
        Builders([.. Production(), Name(typeof(DirectDesignationSentinel))], DesignateCommand, DesignateSetters).ShouldContain(Name(typeof(DirectDesignationSentinel))); // control

        // the owner's command-line designation is built by the verb alone (constructor, `with` or setter), and the registry's designation is asked for by one handler in all of production
        Builders(Production(), OwnerDesignateCommand, OwnerDesignateSetters).ShouldBe(["XDrishti.Api.Cli.LearnDesignateCommand"]);
        Builders([.. Production(), Name(typeof(OwnerDesignationSentinel))], OwnerDesignateCommand, OwnerDesignateSetters).ShouldContain(Name(typeof(OwnerDesignationSentinel))); // control
        Callers(Production(), IsDesignate).ShouldBe(["XDrishti.Application.Learning.DesignateModelHandler"]);
        Callers([.. Production(), Name(typeof(DirectDesignationSentinel))], IsDesignate).ShouldContain(Name(typeof(DirectDesignationSentinel))); // control
    }

    [Fact]
    public void The_rollback_handlers_hold_no_switch_store_and_do_not_read_the_influence_switch_themselves()
    {
        Types.InAssembly(Application).That().HaveName("ApplyRollbackHandler").Should().NotHaveDependencyOnAny(typeof(IMlSwitchStore).FullName!, typeof(IMlInfluenceSwitch).FullName!, "XDrishti.Application.Learning.PromoteChallengerHandler").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().HaveName("ProposeRollbackHandler").Should().NotHaveDependencyOnAny(typeof(IMlSwitchStore).FullName!, typeof(IMlInfluenceSwitch).FullName!, "XDrishti.Application.Learning.PromoteChallengerHandler").GetResult().IsSuccessful.ShouldBeTrue();
    }

    // ---- (2) who writes what ----

    [Fact]
    public void The_switches_have_one_writer_the_owners_handler_and_the_proposals_two_and_each_contract_has_one_implementation_in_infrastructure()
    {
        Dependents(Application, typeof(IMlSwitchStore).FullName!).ShouldBe(["XDrishti.Application.Learning.ChangeMlSettingsHandler"]);
        Callers(Production(), IsSwitchAppend).ShouldBe(["XDrishti.Application.Learning.ChangeMlSettingsHandler"]);
        Dependents(Application, typeof(IRollbackProposalStore).FullName!).ShouldBe(["XDrishti.Application.Learning.ApplyRollbackHandler", "XDrishti.Application.Learning.ProposeRollbackHandler"]);
        Dependents(Application, typeof(IMlMonitorChecks).FullName!).ShouldBe(["XDrishti.Application.Learning.ProposeRollbackHandler", "XDrishti.Application.Learning.ShadowMlInfluenceEvidence"]); // the second only reads the newest row (CF-10e part 10e-2)
        Dependents(Application, typeof(IMlSwitchSettings).FullName!).ShouldBe(
        [
            "XDrishti.Application.Learning.ApplyRollbackHandler", "XDrishti.Application.Learning.ChangeMlSettingsHandler", "XDrishti.Application.Learning.EvaluateMlModelsHandler", "XDrishti.Application.Learning.GetRollbackProposalsHandler",
            "XDrishti.Application.Learning.ModelAutoLookStep", "XDrishti.Application.Learning.ModelHoldOutScorer", // read ml.holdout.auto_score only (CF-10e part 3)
            "XDrishti.Application.Learning.PromoteChallengerHandler", "XDrishti.Application.Learning.ProposeRollbackHandler",
        ]);
        Dependents(Application, typeof(IRollbackProposalReader).FullName!).ShouldBe(
        [
            "XDrishti.Application.Learning.ApplyRollbackHandler", "XDrishti.Application.Learning.GetRollbackProposalsHandler", "XDrishti.Application.Learning.OwnerDesignateModelHandler", "XDrishti.Application.Learning.PromoteChallengerHandler",
            "XDrishti.Application.Learning.ProposeRollbackHandler", "XDrishti.Application.Learning.RollbackProposalNotices",
        ]);
        Dependents(Application, typeof(IRollbackEvidence).FullName!).ShouldBe(["XDrishti.Application.DependencyInjection", "XDrishti.Application.Learning.ProposeRollbackHandler", "XDrishti.Application.Learning.ShadowRollbackEvidence"]);
        Dependents(Application, typeof(IChallengerEvidence).FullName!).ShouldBe(["XDrishti.Application.DependencyInjection", "XDrishti.Application.Learning.NoChallengerEvidence", "XDrishti.Application.Learning.PromoteChallengerHandler"]);

        foreach (var (contract, namespaceName) in new[]
        {
            (typeof(IMlSwitchStore), "XDrishti.Infrastructure.Learning"), (typeof(IMlSwitchSettings), "XDrishti.Infrastructure.Learning"), (typeof(IRollbackProposalStore), "XDrishti.Infrastructure.Learning"),
            (typeof(IRollbackProposalReader), "XDrishti.Infrastructure.Learning"), (typeof(IMlMonitorChecks), "XDrishti.Infrastructure.Learning"),
        })
        {
            var implementations = ProductionNames.SelectMany(n => Assembly.Load(new AssemblyName(n)).GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && contract.IsAssignableFrom(t)).Select(t => t.Namespace).Distinct().ToList();
            implementations.ShouldBe([namespaceName], contract.Name);
        }

        // the evidence ports have exactly one implementation each, and it says that nothing is available
        foreach (var (contract, implementation) in new[] { (typeof(IRollbackEvidence), "ShadowRollbackEvidence"), (typeof(IChallengerEvidence), "NoChallengerEvidence") })
        {
            ProductionNames.SelectMany(n => Assembly.Load(new AssemblyName(n)).GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && contract.IsAssignableFrom(t)).Select(t => t.Name).ShouldBe([implementation], contract.Name);
        }

        // controls: the writer scan sees a second writer of the switch, and a reader of the proposals is seen
        Dependents(Self, typeof(IMlSwitchStore).FullName!).ShouldContain(Name(typeof(SwitchWriterSentinel)));
        Dependents(Self, typeof(IRollbackProposalReader).FullName!).ShouldContain(Name(typeof(ProposalReaderSentinel)));
    }

    [Fact]
    public void The_switches_are_stored_state_the_stamped_ones_and_no_configuration_section_holds_them()
    {
        foreach (var file in new[] { "ConfidenceSettings.cs" })
        {
            var settings = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Confidence", file));
            settings.ShouldNotContain("AutoPromote");
            settings.ShouldNotContain("AutoRollback");
            settings.ShouldNotContain("InfluenceEnabled");
        }

        MlSwitch.Names.ShouldBe(["ml.influence.enabled", "ml.auto_promote", "ml.auto_rollback"]);
        MlSwitches.AllOff.ShouldBe(new MlSwitches(new MlSwitchState(null, false), new MlSwitchState(null, false), new MlSwitchState(null, false)));
        MlActors.Auto.ShouldBe("auto");
    }

    // ---- (3) who promotes ----

    [Fact]
    public void The_promotion_command_is_built_by_the_daily_run_as_auto_and_by_the_owners_route_and_nothing_else_so_an_automatic_path_cannot_re_promote()
    {
        var production = Production();

        Builders(production, PromoteCommand, PromoteSetters).ShouldBe(["XDrishti.Api.Endpoints.MlSafetyEndpoints", "XDrishti.Application.Learning.EvaluateMlModelsHandler", "XDrishti.Application.Learning.OwnerDesignateModelHandler"]); // the designate verb asks for a promotion when a champion exists (CF-10e D6, ADR 0281)
        Builders([.. production, Name(typeof(PromotionBuilderSentinel))], PromoteCommand, PromoteSetters).ShouldContain(Name(typeof(PromotionBuilderSentinel))); // control
        Builders([.. production, Name(typeof(PromotionWithSentinel))], PromoteCommand, PromoteSetters).ShouldContain(Name(typeof(PromotionWithSentinel))); // control: a `with` expression

        // the run offers the actor auto and only challengers; the endpoint offers the session's owner
        var run = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Learning", "EvaluateMlModelsHandler.cs"));
        run.ShouldContain("MlActors.Auto");
        run.ShouldContain("ModelRole.Challenger");
        run.ShouldNotContain("ModelRole.Retired");

        // the daily run reaches neither the owner's switch writer nor the owner's CLI designation nor a revert
        var closure = Closure(["XDrishti.Application.Learning.MlMonitorService", "XDrishti.Application.Learning.EvaluateMlModelsHandler"]);
        closure.ShouldContain("XDrishti.Application.Learning.PromoteChallengerHandler");
        closure.ShouldContain("XDrishti.Application.Learning.DemoteModelHandler");
        closure.ShouldNotContain("XDrishti.Application.Learning.ChangeMlSettingsHandler");
        closure.ShouldNotContain("XDrishti.Application.Learning.OwnerDesignateModelHandler");
        closure.ShouldNotContain("XDrishti.Api.Cli.LearnDesignateCommand");
        closure.ShouldNotContain("XDrishti.Application.Learning.RevertChangelogHandler");
        Callers(closure, IsSwitchAppend).ShouldBeEmpty();
    }

    [Fact]
    public void Promotion_reads_the_champion_rule_and_the_promotion_rule_and_sets_no_switch()
    {
        Types.InAssembly(Application).That().HaveName("PromoteChallengerHandler").Should().HaveDependencyOnAll(typeof(MlPromotion).FullName!, typeof(ChampionRule).FullName!, typeof(IChallengerEvidence).FullName!).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().HaveName("PromoteChallengerHandler").Should().NotHaveDependencyOnAny(typeof(IMlSwitchStore).FullName!, typeof(IMlInfluenceSwitch).FullName!).GetResult().IsSuccessful.ShouldBeTrue();
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Learning", "PromoteChallengerHandler.cs")).ShouldNotContain("AppendAsync");
    }

    // ---- (4) nothing that chooses names this slice ----

    [Fact]
    public void Planning_selection_and_autonomy_name_nothing_of_the_rollback_the_promotion_or_the_switches()
    {
        (Assembly Assembly, string Namespace)[] choosers =
        [
            (Domain, "XDrishti.Domain.Selection"), (Domain, "XDrishti.Domain.Planning"), (Domain, "XDrishti.Domain.Autonomy"),
            (Application, "XDrishti.Application.Planning"), (Application, "XDrishti.Application.Selection"), (Application, "XDrishti.Application.Autonomy"),
        ];

        foreach (var (assembly, ns) in choosers)
        {
            var types = Types.InAssembly(assembly).That().ResideInNamespace(ns);
            types.GetTypes().ShouldNotBeEmpty(ns);
            var result = types.Should().NotHaveDependencyOnAny(SliceTypes).GetResult();
            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        Types.InAssembly(Self).That().HaveName(nameof(ProposalReaderSentinel)).Should().NotHaveDependencyOnAny(SliceTypes).GetResult().IsSuccessful.ShouldBeFalse("control: the rule sees a reader of proposals");
        Types.InAssembly(Self).That().HaveName(nameof(SwitchWriterSentinel)).Should().NotHaveDependencyOnAny(SliceTypes).GetResult().IsSuccessful.ShouldBeFalse("control: the rule sees a writer of a switch");
    }

    // ---- (5) no order ----

    [Fact]
    public void Nothing_of_the_slice_touches_an_order_the_execution_plane_or_a_broker()
    {
        string[] forbidden =
        [
            "XDrishti.Execution", "XDrishti.Application.Execution", "XDrishti.Application.Abstractions.Execution", "XDrishti.Application.Abstractions.LiveTrading", "XDrishti.Application.LiveTrading",
            "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Accounts", "XDrishti.Application.Autonomy", "XDrishti.Application.Abstractions.Autonomy", "XDrishti.Application.Abstractions.Brokers",
        ];
        var ours = Types.InAssembly(Application).That().HaveNameMatching("^(PromoteChallenger|DemoteModel|ProposeRollback|ApplyRollback|ChangeMlSettings|EvaluateMlModels|MlMonitorService|GetRollbackProposals|RollbackProposalNotices|PromotionNotices|OwnerDesignateModel|NoRollbackEvidence|NoChallengerEvidence|MlSafety|MlActors|MlRunOutcome|PromotionOutcome).*$");

        ours.GetTypes().Count().ShouldBeGreaterThan(15);
        var result = ours.Should().NotHaveDependencyOnAny(forbidden).GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        var domain = Types.InAssembly(Domain).That().HaveNameMatching("^(MlPromotion|MlPromotionFacts|MlPromotionResult|RollbackPolicy|RollbackDecision|RollbackTrigger|RollbackInputs|RollbackCellSeries|RegimeCellRule|MlSwitch)$");
        domain.GetTypes().Count().ShouldBe(10);
        domain.Should().NotHaveDependencyOnAny([.. forbidden, "System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.TimeProvider", "System.Random", "Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Application", "XDrishti.Infrastructure"]).GetResult().IsSuccessful.ShouldBeTrue();
        foreach (var store in new[] { "RollbackProposals", "MlSwitchSettings", "MlMonitorChecks" })
        {
            Types.InAssembly(typeof(XDrishti.Infrastructure.DependencyInjection).Assembly).That().HaveName(store).Should().NotHaveDependencyOnAny("XDrishti.Execution", "XDrishti.Application.Planning").GetResult().IsSuccessful.ShouldBeTrue();
        }
    }

    // ---- (6) the trigger is the P6-09 detector, not a copy of its numbers ----

    [Fact]
    public void The_rollback_trigger_is_the_p6_09_detector_itself_and_the_policy_holds_no_threshold_of_its_own()
    {
        Types.InAssembly(Domain).That().HaveName("RollbackPolicy").Should().HaveDependencyOnAll("XDrishti.Domain.Learning.CalibrationDriftDetector", "XDrishti.Domain.Learning.RollingBandDetector").GetResult().IsSuccessful.ShouldBeTrue();
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Learning", "RollbackPolicy.cs"));

        foreach (var arithmetic in new[] { "Math.Sqrt", "Math.Abs", "CalibrationZ", "MaxCalibrationGapPp", "BelowBandChecks", "CalibrationBreachChecks", "BandZ", "WindowTrades" })
        {
            source.ShouldNotContain(arithmetic, Case.Sensitive, "the thresholds live in the detectors and in DriftOptions");
        }

        source.ShouldContain("CalibrationDriftDetector.Run");
        source.ShouldContain("RollingBandDetector.MakeBand");
        source.ShouldContain("RollingBandDetector.Run");
    }

    // ---- (7) the routes ----

    [Fact]
    public void The_routes_are_the_owners_apply_needs_the_session_and_the_switch_and_the_promotion_need_a_fresh_password_where_reliance_on_ml_may_rise()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "MlSafetyEndpoints.cs"));

        source.Split("MapGet(").Length.ShouldBe(2);
        source.Split("MapPost(").Length.ShouldBe(4, "apply, promote and the hold-out score");
        source.Split("MapPut(").Length.ShouldBe(2, "the switches");
        foreach (var verb in new[] { "MapDelete", "MapPatch", "MapMethods" })
        {
            source.ShouldNotContain(verb);
        }

        source.ShouldContain("/rollback/proposals");
        source.ShouldContain("/rollback/{id:long}/apply");
        source.ShouldContain("/ml-settings");
        source.ShouldContain("/models/{id:long}/promote");
        source.ShouldContain("/models/{id:long}/holdout-score");
        source.Split("OwnerAct.FreshAsync(").Length.ShouldBe(4, "the switch change that raises reliance on ML (or changes the auto hold-out score), the promotion and the hold-out score");
        source.ShouldContain("MlSwitch.NeedsFreshPassword(");
        source.Split("OwnerAct.SessionOwner(http)").Length.ShouldBe(3, "apply, and the switch change that only lowers reliance");
        source.ShouldNotContain("request.Actor");
        source.ShouldNotContain("request.Owner");
    }

    [Fact]
    public void The_migration_is_one_added_file()
    {
        var infrastructure = RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "XDrishti.Infrastructure.csproj");
        var migrations = Path.Combine(Path.GetDirectoryName(infrastructure)!, "Persistence", "Migrations");

        Directory.GetFiles(migrations, "*_AddMlRollback.cs").Length.ShouldBe(1);
        Directory.GetFiles(migrations, "*_AddMlRollback.Designer.cs").Length.ShouldBe(1);
    }
}
