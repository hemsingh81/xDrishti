using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Research;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// P3-10, criterion 16: the result store keeps the layers. The port and its types are in Application and name no database or engine type; the adapter and its rows are internal to Infrastructure; the handlers are internal and sealed; the only
/// code that writes the research tables is the adapter and the migration, and no module outside <c>Infrastructure/Research</c> (and the mapping and migrations of <c>Infrastructure/Persistence</c>) names them. Each rule has a sentinel, a case the same
/// rule must reject.
/// </summary>
public sealed partial class ResultStoreArchitectureTests
{
    private const string PortNamespace = "XDrishti.Application.Abstractions.Research";

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] Forbidden = ["Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Infrastructure", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Indicators", "XDrishti.SimExport"];

    [GeneratedRegex(@"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM|COPY|TRUNCATE(\s+TABLE)?)\s+research\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WritesResearch();

    [GeneratedRegex(@"\bsim_(runs|trades)\b", RegexOptions.CultureInvariant)]
    private static partial Regex NamesTheTables();

    private static string Source => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))! + Path.DirectorySeparatorChar + "src";

    private static List<string> SourceFiles() =>
        [.. Directory.GetFiles(Source, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f))];

    /// <summary>The code of a file without its comment lines (the docs of the port may name the tables).</summary>
    private static string Code(string file) => string.Join('\n', File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string Relative(string file) => Path.GetRelativePath(Source, file).Replace(Path.DirectorySeparatorChar, '/');

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void The_port_and_its_types_name_no_database_engine_or_library_type()
    {
        var types = Types.InAssembly(Application).That().ResideInNamespace(PortNamespace).GetTypes().Select(t => t.Name).ToList();

        types.ShouldContain("IRunStore");
        types.ShouldContain("RunToStore");
        types.ShouldContain("StoredRunHeader");
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace(PortNamespace).ShouldNot().HaveDependencyOnAny(Forbidden).GetResult());
        typeof(IRunStore).IsPublic.ShouldBeTrue();
    }

    [Fact]
    public void The_port_rule_does_catch_a_type_that_names_npgsql()
    {
        Types.InAssembly(typeof(NpgsqlUserSentinel).Assembly).That().HaveName(nameof(NpgsqlUserSentinel)).ShouldNot().HaveDependencyOnAny(Forbidden).GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void The_adapter_and_its_rows_are_internal_and_the_adapter_is_sealed_and_implements_the_port()
    {
        foreach (var name in new[] { "RunStore", "SimRunRow", "SimTradeRow", "TradeColumns", "TradeColumn" })
        {
            var type = Infrastructure.GetType("XDrishti.Infrastructure.Research." + name);

            type.ShouldNotBeNull(name);
            type.IsPublic.ShouldBeFalse(name + " is internal");
            type.IsSealed.ShouldBeTrue(name + " is sealed (or static)");
        }

        Infrastructure.GetType("XDrishti.Infrastructure.Research.RunStore")!.GetInterfaces().ShouldContain(typeof(IRunStore));
    }

    [Fact]
    public void The_handlers_are_internal_sealed_and_registered_in_the_composition_root()
    {
        foreach (var name in new[] { "StoreRunHandler", "GetStoredRunHandler", "ListStoredRunsHandler", "PageStoredTradesHandler" })
        {
            var type = Application.GetType("XDrishti.Application.Research." + name);

            type.ShouldNotBeNull(name);
            type.IsPublic.ShouldBeFalse(name + " is internal");
            type.IsSealed.ShouldBeTrue(name + " is sealed");
        }

        var registration = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "DependencyInjection.cs"));
        registration.ShouldContain("StoreRunHandler>()");
        registration.ShouldContain("GetStoredRunHandler>()");
        registration.ShouldContain("ListStoredRunsHandler>()");
        registration.ShouldContain("PageStoredTradesHandler>()");
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "DependencyInjection.cs")).ShouldContain("AddScoped<IRunStore, RunStore>()");
    }

    [Fact]
    public void The_only_code_that_writes_the_research_tables_is_the_adapter_and_the_migration()
    {
        var writers = SourceFiles().Where(f => WritesResearch().IsMatch(Code(f))).Select(Relative).Order(StringComparer.Ordinal).ToList();

        writers.ShouldBe(["XDrishti.Infrastructure/Research/ExitSelectionStore.cs", "XDrishti.Infrastructure/Research/RunStore.Comparisons.cs", "XDrishti.Infrastructure/Research/RunStore.Legs.cs", "XDrishti.Infrastructure/Research/RunStore.Write.cs"]);
        WritesResearch().IsMatch("INSERT INTO research.sim_runs (a) VALUES (1)").ShouldBeTrue("the scan does see a write");
        WritesResearch().IsMatch("COPY research.sim_trades (a) FROM STDIN").ShouldBeTrue();
        WritesResearch().IsMatch("DELETE FROM research.sim_runs").ShouldBeTrue();
        WritesResearch().IsMatch("SELECT * FROM research.sim_runs").ShouldBeFalse("a read is not a write");
    }

    [Fact]
    public void No_module_outside_the_research_adapter_the_mapping_and_the_migrations_names_the_research_tables()
    {
        var users = SourceFiles()
            .Where(f => NamesTheTables().IsMatch(Code(f)) && !Relative(f).StartsWith("XDrishti.Infrastructure/Research/", StringComparison.Ordinal) && !Relative(f).StartsWith("XDrishti.Infrastructure/Persistence/", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        users.ShouldBeEmpty();
        NamesTheTables().IsMatch("FROM research.sim_trades").ShouldBeTrue("the scan does see a use");
        SourceFiles().Where(f => File.ReadAllText(f).Contains("Schemas.Research", StringComparison.Ordinal)).Select(Relative).ShouldAllBe(f => f.StartsWith("XDrishti.Infrastructure/Persistence/", StringComparison.Ordinal));
    }

    [Fact]
    public void The_four_store_events_have_the_ids_4415_to_4418_and_the_spec_range_is_free_of_others()
    {
        var ids = Application.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.DeclaringType!.Name == "StoreLog")
            .SelectMany(m => m.GetCustomAttributes<LoggerMessageAttribute>().Select(a => a.EventId))
            .Order()
            .ToList();

        ids.ShouldBe([4415, 4416, 4417, 4418]);
    }
}

/// <summary>Names a database type on purpose, to prove the rule that keeps the store port free of one sees such a use (it is never used by anything else).</summary>
internal static class NpgsqlUserSentinel
{
    public static string Name() => typeof(Npgsql.NpgsqlConnection).Name;
}
