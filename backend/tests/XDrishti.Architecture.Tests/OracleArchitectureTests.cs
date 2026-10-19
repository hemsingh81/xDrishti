using System.Reflection;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// ADR 0047, spec P3-08 D2 (d) and (e): the independent oracle is a plain library under <c>tests/</c> that references nothing of xDrishti, so that its independence from the engine can be checked and not only claimed. It has no project and no
/// package reference and imports no <c>Tests.props</c>; its assembly references no <c>XDrishti.*</c> assembly (type-dependency scans, NetArchTest's, would not see this); no <c>src/</c> project references it; the only projects that do are the
/// comparison (<c>XDrishti.Simulation.Tests</c>) and this one (which must load it to inspect it: the stated exception); it is in the solution and never copied into an image; it uses no clock, file or random numbers; and no production comparison
/// passes a non-empty <c>OracleFaults</c> (IL scan). Each rule has a sentinel, a case the same code must reject, so that a rule that finds nothing cannot pass for the wrong reason.
/// </summary>
public sealed class OracleArchitectureTests
{
    private const string OracleName = "XDrishti.SimOracle";

    private const string FaultsType = "XDrishti.SimOracle.OracleFaults";

    private const string CostFaultsType = "XDrishti.SimOracle.OracleCostFaults";

    /// <summary>The two fault sets of the oracle: the walker's and (P3-08c) the costing's; a production comparison passes neither.</summary>
    private static readonly string[] FaultsTypes = [FaultsType, CostFaultsType];

    private static readonly string[] AllowedUsers = ["XDrishti.Architecture.Tests", "XDrishti.Simulation.Tests"];

    private static readonly Assembly Oracle = Assembly.Load(new AssemblyName(OracleName));

    private static string BackendRoot => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!;

    private static XDocument ProjectFile(string folder, string name) => XDocument.Load(RepositoryFiles.RepositoryFile("backend", folder, name, name + ".csproj"));

    // (a) the project file

    /// <summary>The reasons a project file is not a library that references nothing: a project, a package, a plain assembly reference or an import (the shared test props bring packages).</summary>
    private static List<string> ReferenceViolations(XDocument project) =>
    [
        .. project.Descendants("ProjectReference").Select(r => "project " + (string?)r.Attribute("Include")),
        .. project.Descendants("PackageReference").Select(r => "package " + (string?)r.Attribute("Include")),
        .. project.Descendants("Reference").Select(r => "assembly " + (string?)r.Attribute("Include")),
        .. project.Descendants("Import").Select(r => "import " + (string?)r.Attribute("Project")),
    ];

    [Fact]
    public void The_oracle_project_has_no_project_package_assembly_or_props_reference()
    {
        ReferenceViolations(ProjectFile("tests", OracleName)).ShouldBeEmpty();
    }

    [Fact]
    public void The_project_file_rule_does_reject_a_project_that_references_something()
    {
        // the sentinels: the comparison project references a project and imports the shared test props (packages); the rule must name both
        var violations = ReferenceViolations(ProjectFile("tests", "XDrishti.Simulation.Tests"));

        violations.ShouldContain(v => v.StartsWith("project ", StringComparison.Ordinal));
        violations.ShouldContain(v => v.StartsWith("import ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_oracle_is_a_plain_library_and_not_a_test_project()
    {
        // Directory.Build.props makes a project whose name ends with "Tests" an executable test project; the oracle holds no test of its own
        OracleName.EndsWith("Tests", StringComparison.Ordinal).ShouldBeFalse();
        Oracle.GetName().Name.ShouldBe(OracleName);
        Oracle.EntryPoint.ShouldBeNull();
    }

    // (b) the compiled assembly

    private static List<string> XDrishtiReferences(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).Where(n => n.StartsWith("XDrishti.", StringComparison.Ordinal))];

    [Fact]
    public void The_oracle_assembly_references_no_xdrishti_assembly()
    {
        XDrishtiReferences(Oracle).ShouldBeEmpty();
    }

    [Fact]
    public void The_assembly_rule_does_see_a_reference_to_a_project_of_the_solution()
    {
        // the engine's library references the Domain: the same scan must list it, and so must the comparison's own assembly (sentinels)
        XDrishtiReferences(typeof(XDrishti.Simulation.SimulationEngine).Assembly).ShouldContain("XDrishti.Domain");
        XDrishtiReferences(typeof(OracleArchitectureTests).Assembly).ShouldContain("XDrishti.Simulation");
    }

    // (c) who references the oracle

    private static List<string> ProjectsThatReference(string root, string library) =>
        [.. Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !RepositoryFiles.IsBuildOutput(file) && RepositoryFiles.ProjectReferences(XDocument.Load(file)).Contains(library))
            .Select(file => Path.GetFileNameWithoutExtension(file))
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void Only_the_comparison_and_the_architecture_tests_reference_the_oracle_and_no_source_project_does()
    {
        // the exception is stated here: this project loads the assembly to inspect it, so it is the second referrer, beside the comparison
        ProjectsThatReference(BackendRoot, OracleName).ShouldBe(AllowedUsers);
        ProjectsThatReference(Path.Combine(BackendRoot, "src"), OracleName).ShouldBeEmpty();
    }

    [Fact]
    public void The_reference_scan_does_see_references_in_the_tests_folder()
    {
        // the same scan for a library the comparison has always used: if it found nothing, the rule above would pass for the wrong reason
        ProjectsThatReference(BackendRoot, "XDrishti.Simulation").ShouldContain("XDrishti.Simulation.Tests");
    }

    [Fact]
    public void The_oracle_is_in_the_solution_and_is_never_copied_into_an_image()
    {
        var solution = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"));
        var dockerfile = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "Dockerfile"));

        solution.ShouldContain("tests/XDrishti.SimOracle/XDrishti.SimOracle.csproj");
        dockerfile.ShouldNotContain(OracleName, Case.Sensitive, "a test library is not shipped");
    }

    // (d) no clock, no file, no random numbers

    private static readonly string[] AmbientTypes =
        ["System.Random", "System.IO.File", "System.IO.Directory", "System.IO.Path", "System.Environment", "System.Diagnostics.Stopwatch", "System.TimeProvider", "System.Net.Http.HttpClient"];

    private static List<string> AmbientUses(Assembly assembly) =>
        [.. AssemblyScan.MemberReferences(assembly)
            .Where(c => AmbientTypes.Contains(c.Type) || (c.Type is "System.DateTime" or "System.DateTimeOffset" && c.Member is "" or "get_Now" or "get_UtcNow" or "get_Today"))
            .Select(c => c.Type + "." + c.Member)
            .Distinct()];

    [Fact]
    public void The_oracle_reads_no_clock_file_environment_or_random_numbers()
    {
        AmbientUses(Oracle).ShouldBeEmpty();
    }

    [Fact]
    public void The_ambient_rule_does_catch_an_assembly_that_reads_files()
    {
        AmbientUses(typeof(OracleArchitectureTests).Assembly).ShouldContain(use => use.StartsWith("System.IO.File", StringComparison.Ordinal));
    }

    // (e) no production comparison passes faults

    /// <summary>
    /// The calls, in the types of a module that match <paramref name="inScope"/>, to a method that has a parameter of type <c>OracleFaults</c> where the argument is not the literal 0 (<c>None</c>) - a constant of a flags enum is inlined by the
    /// compiler, so the IL pattern is the load of the argument just before the call: <c>ldc.i4.0</c> (then <c>conv.i8</c>) or <c>ldc.i8 0</c> is the empty set; anything else (another constant, a local, an argument being forwarded) is not.
    /// <c>AssemblyScan.MembersUsedBy</c> (the member scan the other rules use) lists what a type calls and cannot say what was passed, so this rule walks the same IL with the same library (Mono.Cecil) and looks at the instruction before the call.
    /// </summary>
    private static List<string> NonEmptyFaultArguments(ModuleDefinition module, Func<TypeDefinition, bool> inScope)
    {
        var found = new List<string>();
        foreach (var type in AllTypes(module).Where(inScope))
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var call in method.Body.Instructions.Where(i => i.Operand is MethodReference called && called.Parameters.Any(p => FaultsTypes.Contains(p.ParameterType.FullName))))
                {
                    var called = (MethodReference)call.Operand;
                    if (!IsLiteralZero(FaultsArgument(call, called)))
                    {
                        found.Add(type.FullName + "." + method.Name + " calls " + called.DeclaringType.Name + "." + called.Name + " with faults that are not the literal None");
                    }
                }
            }
        }

        return found;
    }

    /// <summary>How many calls in the types that match <paramref name="inScope"/> take a faults argument at all (the literal None included): the number the rule above looked at.</summary>
    private static int CallsWithFaults(ModuleDefinition module, Func<TypeDefinition, bool> inScope) =>
        AllTypes(module).Where(inScope).SelectMany(t => t.Methods.Where(m => m.HasBody)).SelectMany(m => m.Body.Instructions).Count(i => i.Operand is MethodReference called && called.Parameters.Any(p => FaultsTypes.Contains(p.ParameterType.FullName)));

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        var pending = new Stack<TypeDefinition>(module.Types);
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            yield return type;
            foreach (var nested in type.NestedTypes)
            {
                pending.Push(nested);
            }
        }
    }

    /// <summary>
    /// The instruction that loads the faults argument of a call: the one just before the call when the faults parameter is the last, and, when other parameters follow it (the costing's <c>Leg</c> has a trace after its faults), the one that
    /// many single-instruction loads further back. An argument that follows and is not a single load (a call, an expression) leaves the faults argument unknown, which the caller reads as "not the literal None".
    /// </summary>
    private static Instruction? FaultsArgument(Instruction call, MethodReference called)
    {
        var index = called.Parameters.ToList().FindIndex(p => FaultsTypes.Contains(p.ParameterType.FullName));
        var instruction = call.Previous;
        for (var following = called.Parameters.Count - 1 - index; following > 0; following--)
        {
            if (instruction is null || !IsSingleLoad(instruction))
            {
                return null;
            }

            instruction = instruction.Previous;
        }

        return instruction;
    }

    private static bool IsSingleLoad(Instruction instruction) =>
        instruction.OpCode.Code is Code.Ldnull or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_M1 or Code.Ldc_I4 or Code.Ldc_I4_S or Code.Ldc_I8 or Code.Ldc_R8 or Code.Ldstr
            or Code.Ldloc or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldloc_S or Code.Ldarg or Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg_S or Code.Ldsfld;

    /// <summary>The outermost type of a type: a lambda or a closure inside a fault test is compiled into a nested type, and it is the fault test's.</summary>
    private static string OutermostName(TypeDefinition type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type.Name;
    }

    private static bool IsLiteralZero(Instruction? instruction)
    {
        if (instruction is null)
        {
            return false;
        }

        if (instruction is { OpCode.Code: Code.Conv_I8 or Code.Conv_U8 })
        {
            instruction = instruction.Previous;
        }

        return instruction is { OpCode.Code: Code.Ldc_I4_0 } or { OpCode.Code: Code.Ldc_I4, Operand: 0 } or { OpCode.Code: Code.Ldc_I8, Operand: 0L };
    }

    /// <summary>A small assembly built in memory, with the shapes of the rule: the fault-set type, the oracle's entry and three callers (honest, faulty, forwarding).</summary>
    private static MemoryStream SentinelAssembly(string faultsTypeName = "OracleFaults")
    {
        var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("OracleSentinel", new Version(1, 0)), "OracleSentinel", ModuleKind.Dll);
        var module = assembly.MainModule;
        var faults = new TypeDefinition("XDrishti.SimOracle", faultsTypeName, TypeAttributes.Public | TypeAttributes.Sealed, module.ImportReference(typeof(Enum)));
        faults.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, module.TypeSystem.UInt64));
        module.Types.Add(faults);

        var walker = new TypeDefinition("XDrishti.SimOracle", "ReferenceWalker", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        var walk = new MethodDefinition("Walk", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        walk.Parameters.Add(new ParameterDefinition("faults", ParameterAttributes.None, faults));
        walk.Body.GetILProcessor().Emit(OpCodes.Ret);
        walker.Methods.Add(walk);

        // the costing's shape: the faults are followed by another parameter (a trace), so the argument is one load further back
        var walkMiddle = new MethodDefinition("WalkMiddle", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        walkMiddle.Parameters.Add(new ParameterDefinition("faults", ParameterAttributes.None, faults));
        walkMiddle.Parameters.Add(new ParameterDefinition("trace", ParameterAttributes.None, module.TypeSystem.Object));
        walkMiddle.Body.GetILProcessor().Emit(OpCodes.Ret);
        walker.Methods.Add(walkMiddle);
        module.Types.Add(walker);

        AddCaller(module, faults, walk, "Honest", il => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Conv_I8); });
        AddCaller(module, faults, walk, "Faulty", il => { il.Emit(OpCodes.Ldc_I4, 5); il.Emit(OpCodes.Conv_I8); });
        AddCaller(module, faults, walk, "Forwarding", il => il.Emit(OpCodes.Ldarg_0), forwards: true);
        AddCaller(module, faults, walkMiddle, "HonestMiddle", il => { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Conv_I8); }, trailingNull: true);
        AddCaller(module, faults, walkMiddle, "FaultyMiddle", il => { il.Emit(OpCodes.Ldc_I4, 5); il.Emit(OpCodes.Conv_I8); }, trailingNull: true);

        var stream = new MemoryStream();
        assembly.Write(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddCaller(ModuleDefinition module, TypeDefinition faults, MethodDefinition walk, string name, Action<ILProcessor> loadArgument, bool forwards = false, bool trailingNull = false)
    {
        var type = new TypeDefinition("Sentinel", name, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        var method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        if (forwards)
        {
            method.Parameters.Add(new ParameterDefinition("faults", ParameterAttributes.None, faults));
        }

        var il = method.Body.GetILProcessor();
        loadArgument(il);
        if (trailingNull)
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, walk);
        il.Emit(OpCodes.Ret);
        type.Methods.Add(method);
        module.Types.Add(type);
    }

    [Fact]
    public void The_fault_rule_accepts_a_caller_that_passes_none_and_rejects_a_caller_that_passes_faults_or_forwards_them()
    {
        using var stream = SentinelAssembly();
        using var module = ModuleDefinition.ReadModule(stream);

        NonEmptyFaultArguments(module, t => t.Name == "Honest").ShouldBeEmpty();
        NonEmptyFaultArguments(module, t => t.Name == "Faulty").Count.ShouldBe(1);
        NonEmptyFaultArguments(module, t => t.Name == "Forwarding").Count.ShouldBe(1);
        NonEmptyFaultArguments(module, t => t.Name == "HonestMiddle").ShouldBeEmpty();
        NonEmptyFaultArguments(module, t => t.Name == "FaultyMiddle").Count.ShouldBe(1);
    }

    [Fact]
    public void The_fault_rule_covers_the_costing_fault_set_too_the_same_three_callers_give_the_same_answers()
    {
        // P3-08c: the cost reference has its own fault set (OracleCostFaults); the rule must see it, not only the walker's
        using var stream = SentinelAssembly("OracleCostFaults");
        using var module = ModuleDefinition.ReadModule(stream);

        NonEmptyFaultArguments(module, t => t.Name == "Honest").ShouldBeEmpty();
        NonEmptyFaultArguments(module, t => t.Name == "Faulty").Count.ShouldBe(1);
        NonEmptyFaultArguments(module, t => t.Name == "Forwarding").Count.ShouldBe(1);
    }

    [Fact]
    public void No_production_comparison_passes_a_non_empty_set_of_faults()
    {
        // The comparison types are those of the comparison project's Oracle folder, except the tests that run the faults on purpose (their class names end with "FaultTests", spec D8).
        // The assembly is read from the build output of the comparison project (the file is read, not loaded); a checkout that has not built it has nothing to scan here, and the solution build always has.
        var path = Directory.GetFiles(Path.Combine(BackendRoot, "tests", "XDrishti.Simulation.Tests", "bin"), "XDrishti.Simulation.Tests.dll", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        path.ShouldNotBeNull("build XDrishti.Simulation.Tests before this rule is run");

        using var module = ModuleDefinition.ReadModule(path);
        Func<TypeDefinition, bool> inScope = t => t.FullName.StartsWith("XDrishti.Simulation.Tests.Oracle.", StringComparison.Ordinal) && !OutermostName(t).EndsWith("FaultTests", StringComparison.Ordinal);
        var violations = NonEmptyFaultArguments(module, inScope);

        violations.ShouldBeEmpty();

        // non-vacuity (step 2): the scope holds types, and they do call the oracle with the literal None (the rule looked at calls and found them clean), and the types it exempts do pass faults (so the scan can see one)
        AllTypes(module).Count(inScope).ShouldBeGreaterThan(0, "the scan covers no type of the comparison");
        CallsWithFaults(module, inScope).ShouldBeGreaterThan(0, "the comparison calls the oracle with no faults argument at all: the scan has nothing to check");
        NonEmptyFaultArguments(module, t => t.FullName.StartsWith("XDrishti.Simulation.Tests.Oracle.", StringComparison.Ordinal) && OutermostName(t).EndsWith("FaultTests", StringComparison.Ordinal)).ShouldNotBeEmpty("the fault tests pass faults on purpose; a scan that cannot see that finds nothing in production either");
    }
}
