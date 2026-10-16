using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The rules language (P3-02, ADR 0034) is a pure library: it references only Domain and Indicators among the projects and Superpower among the packages, knows no database, framework, HTTP, file or clock, keeps no mutable
/// static state, never turns a string into code (no Reflection.Emit, no Roslyn, no dynamic-LINQ, no assembly loading; the only dynamic code it may ever contain is a System.Linq.Expressions tree built from the
/// validated syntax tree) and every class is sealed. The same compiled condition runs in back-tests and live, so none of those can be allowed to creep in.
/// </summary>
public sealed class StrategiesArchitectureTests
{
    private static readonly Assembly Strategies = typeof(XDrishti.Strategies.Problem).Assembly;

    /// <summary>The abstract bases of the syntax tree: a closed family whose members are all sealed records. Nothing else in the library may be inheritable.</summary>
    private static readonly string[] SyntaxBases = ["SyntaxNode", "Expression", "Argument"];

    private static readonly string[] ForbiddenNamespaces =
    [
        "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "XDrishti.Feed",
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Npgsql", "Serilog",
        "System.Net", "System.IO", "System.Data", "System.Threading.Tasks", "System.Diagnostics",
        "System.TimeProvider", "System.Environment", "System.Random",
    ];

    /// <summary>Everything that can run text as code or load code by name. <c>System.Linq.Expressions</c> is deliberately not here.</summary>
    private static readonly string[] CodeFromText =
    [
        "System.Reflection.Emit", "System.Reflection.Assembly", "Microsoft.CodeAnalysis", "System.Linq.Dynamic", "System.CodeDom", "System.Runtime.Loader", "System.Activator", "System.AppDomain",
    ];

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void The_library_references_only_domain_and_indicators_among_the_projects_and_superpower_among_the_packages()
    {
        var referenced = Strategies.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        referenced.Where(n => n.StartsWith("XDrishti.", StringComparison.Ordinal)).ShouldBeSubsetOf(["XDrishti.Domain", "XDrishti.Indicators"]);
        referenced.ShouldNotContain(n => n.StartsWith("Microsoft.", StringComparison.Ordinal) && n != "Microsoft.CSharp", "no Microsoft.* package (EF Core, ASP.NET Core, Extensions, Roslyn)");
        referenced.ShouldNotContain(n => n.StartsWith("Npgsql", StringComparison.Ordinal) || n.StartsWith("Serilog", StringComparison.Ordinal));
        referenced.ShouldNotContain("System.Net.Http");
        referenced.ShouldNotContain("System.Net.Primitives");
        referenced.ShouldNotContain(n => n.StartsWith("System.Reflection.Emit", StringComparison.Ordinal));
        referenced.ShouldNotContain(n => n.StartsWith("System.Linq.Dynamic", StringComparison.Ordinal));
        referenced.Where(n => !n.StartsWith("System.", StringComparison.Ordinal) && !n.StartsWith("XDrishti.", StringComparison.Ordinal) && n is not ("netstandard" or "mscorlib" or "Microsoft.CSharp"))
            .ShouldBeSubsetOf(["Superpower"]);
    }

    [Fact]
    public void The_project_file_references_only_the_domain_and_indicators_projects_and_the_superpower_package()
    {
        var project = XDocument.Load(RepositoryFile("backend", "src", "XDrishti.Strategies", "XDrishti.Strategies.csproj"));

        ProjectReferences(project).ShouldBe(["XDrishti.Domain", "XDrishti.Indicators"], "the compiled references can hide a reference the compiler never used; the project file cannot");
        project.Descendants("PackageReference").Select(r => (string?)r.Attribute("Include")).ShouldBe(["Superpower"]);
        project.Descendants("PackageReference").ShouldAllBe(r => r.Attribute("Version") == null, "versions are set once, in Directory.Packages.props");
        project.Descendants("Reference").ShouldBeEmpty();
    }

    [Fact]
    public void Superpower_is_pinned_to_one_exact_version_in_the_central_package_file()
    {
        var packages = XDocument.Load(RepositoryFile("backend", "Directory.Packages.props"));

        var version = packages.Descendants("PackageVersion").Where(p => (string?)p.Attribute("Include") == "Superpower").Select(p => (string?)p.Attribute("Version")).ShouldHaveSingleItem();

        version.ShouldNotBeNull().ShouldMatch(@"^\d+\.\d+\.\d+$", "an exact version, no range, wildcard or pre-release");
    }

    [Fact]
    public void The_projects_are_in_the_solution_and_the_dockerfile_copies_the_library()
    {
        var solution = File.ReadAllText(RepositoryFile("backend", "XDrishti.slnx"));
        var dockerfile = File.ReadAllText(RepositoryFile("backend", "Dockerfile"));

        solution.ShouldContain("src/XDrishti.Strategies/XDrishti.Strategies.csproj");
        solution.ShouldContain("tests/XDrishti.Strategies.Tests/XDrishti.Strategies.Tests.csproj");
        dockerfile.ShouldContain("COPY src/XDrishti.Strategies/XDrishti.Strategies.csproj src/XDrishti.Strategies/");
    }

    [Fact]
    public void The_test_project_references_the_library_and_the_architecture_tests_can_see_it()
    {
        var tests = XDocument.Load(RepositoryFile("backend", "tests", "XDrishti.Strategies.Tests", "XDrishti.Strategies.Tests.csproj"));

        ProjectReferences(tests).ShouldBe(["XDrishti.Strategies"]);
        ProjectReferences(XDocument.Load(RepositoryFile("backend", "tests", "XDrishti.Architecture.Tests", "XDrishti.Architecture.Tests.csproj"))).ShouldContain("XDrishti.Strategies");
    }

    [Fact]
    public void Nothing_below_the_library_depends_on_it()
    {
        var domain = typeof(XDrishti.Domain.Common.Result).Assembly;
        var indicators = typeof(XDrishti.Indicators.IIndicator).Assembly;

        domain.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("XDrishti.Strategies");
        indicators.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain("XDrishti.Strategies");
    }

    [Fact]
    public void A_project_that_uses_the_library_runs_the_compiler_warm_up_when_it_starts()
    {
        // ISS-12, ADR 0034 amendment 2: the first compile of a process takes about 40 ms (the JIT compiling the compiler), 4 ms after CompilerWarmup.Run. No project outside the library references it yet; the first that does must
        // call the warm-up (from a hosted service, on a thread-pool thread), or its first Lab request pays the 40 ms.
        var sourceRoot = Path.Combine(Path.GetDirectoryName(RepositoryFile("backend", "Directory.Packages.props"))!, "src");
        var users = Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(file => Path.GetFileNameWithoutExtension(file) != "XDrishti.Strategies" && ProjectReferences(XDocument.Load(file)).Contains("XDrishti.Strategies"))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();
        var callers = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !IsUnder(file, Path.Combine(sourceRoot, "XDrishti.Strategies")) && CallsTheWarmUp(File.ReadAllText(file)))
            .ToList();

        if (users.Count > 0)
        {
            callers.ShouldNotBeEmpty(string.Join(", ", users) + " reference(s) XDrishti.Strategies, but no source file outside the library calls CompilerWarmup.Run: start it from a hosted service so that the first compile does not pay for the JIT");
        }
    }

    [Fact]
    public void The_warm_up_rule_does_see_a_call_and_a_reference()
    {
        CallsTheWarmUp("_ = Task.Run(CompilerWarmup.Run, cancellationToken);").ShouldBeTrue();
        CallsTheWarmUp("CompilerWarmup.Run();").ShouldBeTrue();
        CallsTheWarmUp("// the compiler is warmed up at start").ShouldBeFalse();
        ProjectReferences(XDocument.Parse("<Project><ItemGroup><ProjectReference Include=\"..\\XDrishti.Strategies\\XDrishti.Strategies.csproj\" /></ItemGroup></Project>")).ShouldBe(["XDrishti.Strategies"]);
    }

    private static bool CallsTheWarmUp(string source) => source.Contains("CompilerWarmup.Run", StringComparison.Ordinal);

    private static bool IsBuildOutput(string file) => file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsUnder(string file, string directory) => file.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static List<string> ProjectReferences(XDocument project) =>
        [.. project.Descendants("ProjectReference").Select(r => Path.GetFileNameWithoutExtension(((string?)r.Attribute("Include") ?? string.Empty).Replace('\\', '/')))];

    /// <summary>Finds a file of the repository by walking up from the test binaries (the tests run from a checkout, never from a published image).</summary>
    private static string RepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("The file " + string.Join('/', parts) + " was not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void No_type_depends_on_other_layers_the_database_http_files_or_a_clock()
    {
        // The compiler's anonymous types (made for LINQ query syntax) carry a debugger attribute from System.Diagnostics; they are not code of ours.
        ShouldHaveNone(Types.InAssembly(Strategies).That().DoNotHaveNameStartingWith("<>f__AnonymousType").ShouldNot().HaveDependencyOnAny(ForbiddenNamespaces).GetResult());
    }

    [Fact]
    public void No_type_can_run_text_as_code_or_load_code_by_name()
    {
        ShouldHaveNone(Types.InAssembly(Strategies).That().DoNotHaveNameStartingWith("<>f__AnonymousType").ShouldNot().HaveDependencyOnAny(CodeFromText).GetResult());
    }

    [Fact]
    public void Nothing_reads_the_clock_or_touches_files_or_the_network_or_loads_code()
    {
        var calls = StrategiesScan.MemberReferences(Strategies);

        calls.Where(c => c.Type is "System.DateTime" or "System.DateTimeOffset" && c.Member is "get_Now" or "get_UtcNow" or "get_Today").ShouldBeEmpty("DateTime.Now / UtcNow / Today");
        calls.Where(c => c.Type is "System.IO.File" or "System.IO.Directory" or "System.Net.Http.HttpClient" or "System.TimeProvider").ShouldBeEmpty("File, Directory, HttpClient, TimeProvider");
        calls.Where(c => c.Type is "System.Diagnostics.Stopwatch" or "System.Environment").ShouldBeEmpty("tick counters");
        calls.Where(c => c.Type is "System.Activator" or "System.AppDomain" or "System.Reflection.Assembly" or "System.Runtime.Loader.AssemblyLoadContext").ShouldBeEmpty("loading code by name");
        calls.Where(c => c.Type.StartsWith("System.Reflection.Emit", StringComparison.Ordinal) || c.Type.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)).ShouldBeEmpty("Reflection.Emit, Roslyn");
        calls.Where(c => c.Type.StartsWith("System.Linq.Dynamic", StringComparison.Ordinal) || c.Type.StartsWith("System.CodeDom", StringComparison.Ordinal)).ShouldBeEmpty("dynamic LINQ, CodeDom");
    }

    [Fact]
    public void The_scan_does_find_a_clock_read_and_the_code_from_text_rule_does_catch_a_violation()
    {
        // ClockSentinel (in this test project) reads DateTime.UtcNow and File.Exists on purpose: the scan used above must see both in an assembly.
        var calls = StrategiesScan.MemberReferences(typeof(ClockSentinel).Assembly);

        calls.ShouldContain(c => c.Type == "System.DateTime" && c.Member == "get_UtcNow");
        calls.ShouldContain(c => c.Type == "System.IO.File");
        Types.InAssembly(typeof(ClockSentinel).Assembly).That().HaveName(nameof(ClockSentinel)).ShouldNot().HaveDependencyOnAny("System.IO").GetResult().IsSuccessful.ShouldBeFalse();
        Types.InAssembly(typeof(CodeFromTextSentinel).Assembly).That().HaveName(nameof(CodeFromTextSentinel)).ShouldNot().HaveDependencyOnAny(CodeFromText).GetResult().IsSuccessful.ShouldBeFalse();
    }

    /// <summary>
    /// The members of the feature frame the library may reference. The doors a compiled condition reads through (the value as of the last closed bar, the value some closed bars ago, the number of steps and the
    /// time of the last bar), what <c>Bind</c> asks (the handle of a feature, the history the frame keeps) and the creation of a frame to let the catalog judge a feature. Nothing that feeds the frame (<c>Step</c>,
    /// <c>Reset</c>) or looks at a bar as it forms (<c>BarClosed</c>, <c>TryLastClosed</c>, <c>ClosedBarsAt</c>, <c>TryGet</c>) is among them: that is the simulator's business, and the reason a condition cannot look ahead.
    /// </summary>
    private static readonly string[] FrameMembers = ["get_Item", "At", "get_Steps", "get_LastBarTime", "HandleOf", "get_HistoryDepth", "Create"];

    [Fact]
    public void The_library_reaches_the_feature_frame_only_through_the_members_of_a_compiled_condition_and_its_binding()
    {
        var used = StrategiesScan.FrameMembersUsed(Strategies);

        used.Except(FrameMembers).ShouldBeEmpty("a member of the frame that is not one of the doors of a condition: 'a condition cannot look ahead' rests on the library using only " + string.Join(", ", FrameMembers));
        FrameMembers.Except(used).ShouldBeEmpty("the scan sees every door the library uses; a rule that finds nothing would pass for the wrong reason");
    }

    [Fact]
    public void The_frame_member_rule_does_catch_a_library_that_feeds_or_peeks_at_the_frame()
    {
        // FrameFeedSentinel (in this test project) calls Step, BarClosed and TryLastClosed on a frame on purpose: the scan used above must see them in an assembly and the rule must refuse them.
        var used = StrategiesScan.FrameMembersUsed(typeof(FrameFeedSentinel).Assembly);

        used.ShouldContain("Step");
        used.ShouldContain("BarClosed");
        used.ShouldContain("TryLastClosed");
        used.Except(FrameMembers).ShouldNotBeEmpty();
    }

    [Fact]
    public void Expression_trees_are_built_by_one_class_only_the_expression_builder_of_the_compilation_namespace()
    {
        // The only dynamic code of the library is a System.Linq.Expressions tree built from the validated, bound syntax tree; keeping it in one class keeps it reviewable.
        ShouldHaveNone(Types.InAssembly(Strategies).That().DoNotHaveName("ExpressionBuilder").And().DoNotHaveNameStartingWith("<>f__AnonymousType").ShouldNot().HaveDependencyOn("System.Linq.Expressions").GetResult());
        Types.InAssembly(Strategies).That().HaveName("ExpressionBuilder").Should().ResideInNamespace("XDrishti.Strategies.Compilation").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Strategies).That().HaveName("ExpressionBuilder").Should().HaveDependencyOn("System.Linq.Expressions").GetResult().IsSuccessful.ShouldBeTrue("the rule finds the builder's own use of expression trees");
    }

    [Fact]
    public void The_expression_tree_rule_does_catch_a_violation()
    {
        Types.InAssembly(typeof(ExpressionTreeSentinel).Assembly).That().HaveName(nameof(ExpressionTreeSentinel)).ShouldNot().HaveDependencyOn("System.Linq.Expressions").GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void No_class_has_mutable_static_state() => StrategiesScan.MutableStatics(Strategies).ShouldBeEmpty("static fields must be const or readonly");

    [Fact]
    public void The_static_state_rule_does_catch_a_violation()
    {
        StrategiesScan.MutableStatics(typeof(ClockSentinel).Assembly).ShouldContain(name => name.EndsWith("ClockSentinel.Counter", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_class_is_sealed_or_static_except_the_closed_family_of_syntax_node_bases()
    {
        var offenders = Strategies.GetTypes()
            .Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null && t.IsClass && !t.IsSealed && !(t.IsAbstract && SyntaxBases.Contains(t.Name) && t.Namespace == "XDrishti.Strategies.Syntax"))
            .Select(t => t.FullName)
            .ToList();

        offenders.ShouldBeEmpty("a class designed for inheritance needs a documented reason; only the abstract bases of the syntax tree have one (the tree is a closed family of sealed records)");
    }

    [Fact]
    public void Every_node_of_the_syntax_tree_is_a_sealed_record_in_the_syntax_namespace()
    {
        var nodes = Strategies.GetTypes().Where(t => typeof(XDrishti.Strategies.Syntax.SyntaxNode).IsAssignableFrom(t) && t != typeof(XDrishti.Strategies.Syntax.SyntaxNode)).ToList();

        nodes.ShouldNotBeEmpty();
        nodes.Where(t => !t.IsSealed && !SyntaxBases.Contains(t.Name)).ShouldBeEmpty("a concrete node is sealed");
        nodes.ShouldAllBe(t => t.Namespace == "XDrishti.Strategies.Syntax");
        nodes.ShouldAllBe(t => t.GetMethod("<Clone>$") != null, "a node is a record, so that a copy with another span is possible and equality is value equality");
    }

    [Fact]
    public void The_public_contract_exposes_no_superpower_type_and_no_other_layer()
    {
        // The signatures, not the method bodies: Superpower is an implementation detail that the public API does not leak, so the parser library could be replaced without changing a caller.
        Leaks(Strategies).ShouldBeEmpty();
    }

    [Fact]
    public void The_leak_rule_does_catch_a_public_signature_that_exposes_a_superpower_type()
    {
        Leaks(typeof(SuperpowerLeakSentinel).Assembly).ShouldContain(leak => leak.StartsWith(typeof(SuperpowerLeakSentinel).FullName + " exposes Superpower.", StringComparison.Ordinal));
    }

    private static List<string> Leaks(Assembly assembly)
    {
        var leaks = new List<string>();
        foreach (var type in assembly.GetExportedTypes())
        {
            var exposed = new List<Type>();
            if (type.BaseType is not null)
            {
                exposed.Add(type.BaseType);
            }

            exposed.AddRange(type.GetInterfaces());
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var isVisible = member switch
                {
                    MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
                    FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
                    PropertyInfo property => (property.GetMethod?.IsPublic ?? false) || (property.GetMethod?.IsFamily ?? false),
                    _ => false,
                };
                if (!isVisible)
                {
                    continue;
                }

                switch (member)
                {
                    case MethodBase method:
                        exposed.AddRange(method.GetParameters().Select(p => p.ParameterType));
                        if (method is MethodInfo info)
                        {
                            exposed.Add(info.ReturnType);
                        }

                        break;
                    case FieldInfo field:
                        exposed.Add(field.FieldType);
                        break;
                    case PropertyInfo property:
                        exposed.Add(property.PropertyType);
                        break;
                }
            }

            leaks.AddRange(exposed.SelectMany(Flatten)
                .Where(t => t.Namespace is { } ns && (ns.StartsWith("Superpower", StringComparison.Ordinal) || ns.StartsWith("XDrishti.Application", StringComparison.Ordinal) || ns.StartsWith("XDrishti.Infrastructure", StringComparison.Ordinal)
                    || ns.StartsWith("Microsoft", StringComparison.Ordinal) || ns.StartsWith("Npgsql", StringComparison.Ordinal)))
                .Select(t => type.FullName + " exposes " + t.FullName));
        }

        return leaks;
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var inner in Flatten(element))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var inner in Flatten(argument))
                {
                    yield return inner;
                }
            }
        }
    }

    [Fact]
    public void Public_types_are_in_the_documented_namespaces()
    {
        var namespaces = Strategies.GetExportedTypes().Select(t => t.Namespace).Distinct().ToList();

        namespaces.ShouldBeSubsetOf(["XDrishti.Strategies", "XDrishti.Strategies.Syntax", "XDrishti.Strategies.Binding", "XDrishti.Strategies.Lint", "XDrishti.Strategies.Compilation"]);
    }

    /// <summary>Reads the member and type references recorded in an assembly's metadata, and finds static fields that can change.</summary>
    private static class StrategiesScan
    {
        public static List<(string Type, string Member)> MemberReferences(Assembly assembly)
        {
            using var stream = File.OpenRead(assembly.Location);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            var found = new List<(string Type, string Member)>();
            foreach (var handle in reader.MemberReferences)
            {
                var member = reader.GetMemberReference(handle);
                if (member.Parent.Kind == HandleKind.TypeReference)
                {
                    var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                    found.Add((reader.GetString(type.Namespace) + "." + reader.GetString(type.Name), reader.GetString(member.Name)));
                }
            }

            foreach (var handle in reader.TypeReferences)
            {
                var type = reader.GetTypeReference(handle);
                found.Add((reader.GetString(type.Namespace) + "." + reader.GetString(type.Name), string.Empty));
            }

            return found;
        }

        /// <summary>The names of the members of <c>FeatureFrame</c> the assembly references (properties as their accessors, <c>get_Steps</c>), each once.</summary>
        public static List<string> FrameMembersUsed(Assembly assembly) =>
            [.. MemberReferences(assembly).Where(c => c.Type == "XDrishti.Indicators.FeatureFrame" && c.Member.Length > 0).Select(c => c.Member).Distinct()];

        public static List<string> MutableStatics(Assembly assembly)
        {
            var offenders = new List<string>();
            foreach (var type in assembly.GetTypes().Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null))
            {
                foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsInitOnly && !field.IsLiteral)
                    {
                        offenders.Add(type.FullName + "." + field.Name);
                    }
                }
            }

            return offenders;
        }
    }
}

/// <summary>Asks for a dynamic assembly on purpose, to prove the rule against code made from text sees such a dependency (it is never used by anything else).</summary>
internal static class CodeFromTextSentinel
{
    public static object Make() => System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("sentinel"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
}

/// <summary>Exposes a Superpower type in a public signature on purpose, to prove the leak rule sees it (it is never used by anything else).</summary>
public sealed class SuperpowerLeakSentinel
{
    public static Superpower.Model.TextSpan Leak() => default;
}

/// <summary>Feeds a frame and looks at the bar being formed on purpose, to prove the rule on the members of the frame sees such a use (it is never used by anything else).</summary>
internal static class FrameFeedSentinel
{
    public static bool Feed(XDrishti.Indicators.FeatureFrame frame, XDrishti.Domain.Market.Bars.Bar bar)
    {
        frame.Step(bar);
        return frame.BarClosed(XDrishti.Domain.Market.Bars.Timeframe.M1) && frame.TryLastClosed(XDrishti.Domain.Market.Bars.Timeframe.M1, out _);
    }
}

/// <summary>Builds an expression tree on purpose, to prove the rule that only the expression builder may do so sees such a dependency (it is never used by anything else).</summary>
internal static class ExpressionTreeSentinel
{
    public static object Make() => System.Linq.Expressions.Expression.Constant(1);
}
