using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning.Jobs;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The replay job started from the screen (spec P8-18 Part B, ADR 0129) can never change configuration and has exactly one way in: the namespace of the job may use only the run store, the experiment ledger, the job store, read ports, the simulation query and the experiment command,
/// and none of the stores of strategies, settings, plans, autonomy, paper, learning state, models or costs; no member of it or of its endpoints is named apply, activate, promote or publish; one endpoint module starts a job, after the capability and a fresh password; the table
/// is granted on its own. Each scan has a sentinel, a case the same code must catch.
/// </summary>
public sealed partial class ExperimentJobArchitectureTests
{
    private const string JobsNamespace = "XDrishti.Application.Learning.Jobs";

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly Assembly Worker = Assembly.Load(new AssemblyName("XDrishti.Worker"));

    private static readonly string Start = typeof(StartExperimentJobCommand).FullName!;

    private static readonly string[] Forbidden =
    [
        "XDrishti.Application.Abstractions.Planning",
        "XDrishti.Application.Abstractions.Autonomy",
        "XDrishti.Application.Abstractions.Paper",
        "XDrishti.Application.Abstractions.Platform",
        "XDrishti.Application.Abstractions.Capabilities.ICapabilityLogWriter",
        "XDrishti.Application.Abstractions.ChargeSheet",
        "XDrishti.Application.Abstractions.Research.IStrategyStore",
        "XDrishti.Application.Abstractions.Research.ICostSheetStore",
        "XDrishti.Application.Abstractions.Research.IExitSelectionStore",
        "XDrishti.Application.Abstractions.Learning.ILearningChangelog",
        "XDrishti.Application.Abstractions.Learning.IHypothesisLog",
        "XDrishti.Application.Abstractions.Learning.ILessonLog",
        "XDrishti.Application.Abstractions.Learning.ICellStatsStore",
        "XDrishti.Application.Abstractions.Learning.IModelRegistry",
        "XDrishti.Application.Abstractions.Learning.IModelFiles",
        "XDrishti.Application.Abstractions.Learning.ITrainingSetStore",
        "XDrishti.Application.Abstractions.Learning.IDigestStore",
        "XDrishti.Application.Abstractions.Lab.ILabRunStore",
        "XDrishti.Application.Abstractions.Lab.ILabResultStore",
        "XDrishti.Application.Lab.StartLabRunCommand",
    ];

    [GeneratedRegex(@"\b(Apply|Activate|Promote|Publish)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Applying();

    [GeneratedRegex(@"\b(DELETE|TRUNCATE|UPDATE)\b\s+(FROM\s+)?(?!research\.experiment_jobs)\w+\.\w+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex WritesElsewhere();

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    private static List<string> Names(IEnumerable<Type> types) => [.. types.Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void The_job_namespace_uses_no_store_of_strategies_settings_plans_autonomy_paper_learning_state_models_or_costs()
    {
        var types = Types.InAssembly(Application).That().ResideInNamespace(JobsNamespace).GetTypes().ToList();

        types.Count.ShouldBeGreaterThan(10, "the scan sees the namespace");
        Types.InAssembly(Application).That().ResideInNamespace(JobsNamespace).ShouldNot().HaveDependencyOnAny(Forbidden).GetResult().IsSuccessful.ShouldBeTrue();
        Names(Types.InAssembly(typeof(StrategyStoreSentinel).Assembly).That().HaveName(nameof(StrategyStoreSentinel)).And().HaveDependencyOnAny(Forbidden).GetTypes()).ShouldBe([nameof(StrategyStoreSentinel)], "the scan sees a use of such a store");
    }

    [Fact]
    public void No_member_of_the_job_the_start_or_its_endpoints_is_named_apply_activate_promote_or_publish()
    {
        var types = Types.InAssembly(Application).That().ResideInNamespace(JobsNamespace).GetTypes()
            .Concat(Types.InAssembly(Api).That().HaveName("ExperimentJobsEndpoints").GetTypes())
            .Concat([typeof(IExperimentJobStore), typeof(IExperimentJobProgress)]).ToList();
        var names = types.SelectMany(t => new[] { t.Name }.Concat(t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name))).ToList();

        names.Count.ShouldBeGreaterThan(50);
        names.Where(n => Applying().IsMatch(n)).ShouldBeEmpty();
        Applying().IsMatch("ApplyChange").ShouldBeTrue("the scan sees such a name");
        Applying().IsMatch("PromoteCell").ShouldBeTrue();
    }

    [Fact]
    public void Only_the_start_endpoint_module_names_the_start_command_and_nothing_in_the_worker_or_the_infrastructure_does()
    {
        Names(Types.InAssembly(Api).That().HaveDependencyOn(Start).GetTypes()).ShouldBe(["ExperimentJobsEndpoints"]);
        Names(Types.InAssembly(Application).That().HaveDependencyOn(Start).GetTypes()).ShouldBe(["DependencyInjection", "StartExperimentJobCommand", "StartExperimentJobHandler"]);
        Types.InAssembly(Infrastructure).That().HaveDependencyOn(Start).GetTypes().ShouldBeEmpty();
        Types.InAssembly(Worker).That().HaveDependencyOn(Start).GetTypes().ShouldBeEmpty();
        Names(Types.InAssembly(typeof(StrategyStoreSentinel).Assembly).That().HaveDependencyOn(Start).GetTypes()).ShouldContain(nameof(StrategyStoreSentinel));
    }

    [Fact]
    public void A_job_is_inserted_only_by_the_start_handler_and_nothing_schedules_or_retries_one()
    {
        var store = typeof(IExperimentJobStore).FullName!;

        Names(Types.InAssembly(Application).That().HaveDependencyOn(store).GetTypes()).ShouldBe(
            ["CancelExperimentJobHandler", "ExperimentJobDispatcher", "ExperimentJobRunner", "GetExperimentJobHandler", "GetExperimentPreviewHandler", "ListExperimentJobsHandler", "StartExperimentJobHandler"]);
        Types.InAssembly(Api).That().HaveDependencyOn(store).GetTypes().ShouldBeEmpty();
        Types.InAssembly(Worker).That().HaveDependencyOn(store).GetTypes().ShouldBeEmpty();
        typeof(IExperimentJobStore).GetMethods().Count(m => m.Name == "InsertAsync").ShouldBe(1);
        Source("XDrishti.Application", "Learning", "Jobs", "ExperimentJobDispatcher.cs").ShouldNotContain("Retry");
        Types.InAssembly(Application).That().ResideInNamespace(JobsNamespace).ShouldNot().HaveDependencyOn("XDrishti.Application.Platform.Services").GetResult().IsSuccessful.ShouldBeTrue("no service schedule starts a job");
    }

    [Fact]
    public void The_endpoint_asks_the_capability_before_the_password_limits_the_attempts_and_cancels_with_the_session_only()
    {
        var text = Source("XDrishti.Api", "Endpoints", "ExperimentJobsEndpoints.cs");

        Regex.Count(text, @"\.MapPost\(", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(2);
        Regex.Count(text, @"\.MapPut\(|\.MapDelete\(|\.MapPatch\(|\.MapMethods\(", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(0);
        Regex.Count(text, @"RequireRateLimiting\(SessionAuthentication\.LoginRateLimitPolicy\)", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(1);
        text.IndexOf("RefusedWhileOffAsync(", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        text.IndexOf("RefusedWhileOffAsync(", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("OwnerAct.AuthorizeAsync(", StringComparison.Ordinal));
        text.ShouldContain("OwnerAct.SessionOwner(http)");
        text.ShouldNotContain("Elevate");
        var dir = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "ExperimentJobsEndpoints.cs"))!;
        Directory.GetFiles(dir, "*.cs").Where(f => !f.EndsWith("ExperimentJobsEndpoints.cs", StringComparison.Ordinal) && !f.EndsWith("ExperimentsEndpoints.cs", StringComparison.Ordinal) && File.ReadAllText(f).Contains("\"/experiments", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void The_owner_is_the_signed_in_session_never_a_field_of_the_start_body()
    {
        foreach (var type in new[] { "StartExperimentRequest", "ExperimentKnobRequest" })
        {
            var request = Api.GetType("XDrishti.Api.Endpoints." + type)!;

            request.GetProperties().Select(p => p.Name).ShouldNotContain("Actor");
            request.GetProperties().Select(p => p.Name).ShouldNotContain("Owner");
        }

        Api.GetType("XDrishti.Api.Endpoints.StartExperimentRequest")!.GetProperties().ShouldContain(p => p.Name == "Password");
    }

    [Fact]
    public void The_stores_write_only_the_job_table_and_the_migration_grants_it_alone_with_the_guards()
    {
        Source("XDrishti.Infrastructure", "Learning", "ExperimentJobStore.cs").ShouldNotContain("DELETE");
        WritesElsewhere().IsMatch(Source("XDrishti.Infrastructure", "Learning", "ExperimentJobStore.cs")).ShouldBeFalse();
        WritesElsewhere().IsMatch("UPDATE research.sim_runs SET").ShouldBeTrue("the scan sees a write elsewhere");
        foreach (var file in new[] { "LabHeavyJobProbe.cs", "StoredReplayStrategySource.cs" })
        {
            Source("XDrishti.Infrastructure", "Learning", file).ShouldNotContain("INSERT");
            Source("XDrishti.Infrastructure", "Learning", file).ShouldNotContain("SaveChanges");
        }

        var migrations = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Persistence", "Migrations", "XDrishtiDbContextModelSnapshot.cs"))!;
        var text = File.ReadAllText(Directory.GetFiles(migrations, "*AddExperimentJobs.cs").Single());
        text.ShouldNotContain("GrantSchemaUsageToAppRole");
        text.ShouldContain("REVOKE UPDATE, DELETE ON research.experiment_jobs FROM xd_app");
        text.ShouldContain("GRANT UPDATE (state, stage");
        text.ShouldContain("uq_experiment_jobs_one_active");
        text.ShouldContain("experiments_ui");
        text.ShouldContain("DROP TRIGGER");
        text.ShouldContain("DROP FUNCTION");
    }

    [Fact]
    public void The_two_approved_writes_under_the_guarded_prefix_are_the_start_and_the_cancel_and_nothing_else()
    {
        XDrishti.Domain.Reporting.ApprovedReportWrites.All.Count.ShouldBe(2);
        XDrishti.Domain.Reporting.ApprovedReportWrites.All.ShouldAllBe(w => w.StartsWith("POST /api/experiments", StringComparison.Ordinal));
    }
}

/// <summary>Uses a store of strategies and the start command on purpose, to prove the scans see such uses (nothing else does).</summary>
internal sealed class StrategyStoreSentinel(XDrishti.Application.Abstractions.Research.IStrategyStore strategies)
{
    public XDrishti.Application.Abstractions.Research.IStrategyStore Strategies { get; } = strategies;

    public StartExperimentJobCommand? Command { get; init; }
}
