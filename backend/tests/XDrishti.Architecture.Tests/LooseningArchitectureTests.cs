using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.LearnedSettings;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Domain.Planning;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// BK-10b criterion 10 (ADR 0265): the loosening banner and its dismissal change no limit and no stored plan. The pure rules do no I/O; no type of the slice calls a sizing, budget or basket-settings change handler or store, the plan record's writers or the plan builder;
/// the automatic dismissal reaches only the dismissal store; the plan builder and the plan store know nothing of the slice (so a banner or a dismissal can never reach <c>input_hash</c>); no type, member or file of the slice names an order, upsize, leverage, margin or multiplier;
/// and the automatic setting is a stored, advanced, never-learnable key with no safer direction.
/// </summary>
public sealed partial class LooseningArchitectureTests
{
    private static readonly Assembly Domain = typeof(LooseningNotices).Assembly;

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly string[] Writers =
    [
        "XDrishti.Application.Abstractions.Selection.ISizingStore",
        "XDrishti.Application.Abstractions.Selection.IBasketSettingsStore",
        "XDrishti.Application.Sizing.ChangeSizingSettingsHandler",
        "XDrishti.Application.Sizing.ChangeBasketRiskBudgetHandler",
        "XDrishti.Application.Selection.ChangeBasketSettingsHandler",
        "XDrishti.Application.Sizing.ChangeSizingSettingsCommand",
        "XDrishti.Application.Sizing.ChangeBasketRiskBudgetCommand",
        "XDrishti.Application.Selection.ChangeBasketSettingsCommand",
        "XDrishti.Application.Abstractions.Planning.IPlanStore",
        "XDrishti.Application.Abstractions.Planning.IPlanBuilder",
        "XDrishti.Application.Planning.SelectionPlanBuilder",
    ];

    private static readonly string[] SliceFiles =
    [
        Path.Combine("XDrishti.Domain", "Planning", "LooseningDirections.cs"),
        Path.Combine("XDrishti.Domain", "Planning", "LooseningNotice.cs"),
        Path.Combine("XDrishti.Domain", "Planning", "LooseningNoticeId.cs"),
        Path.Combine("XDrishti.Domain", "Planning", "LooseningNotices.cs"),
        Path.Combine("XDrishti.Application", "Abstractions", "Planning", "ILooseningStore.cs"),
        Path.Combine("XDrishti.Infrastructure", "Planning", "LooseningStore.cs"),
        Path.Combine("XDrishti.Api", "Endpoints", "LooseningEndpoints.cs"),
    ];

    [GeneratedRegex("upsiz|leverag|margin|multiplier", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Banned();

    private static IEnumerable<string> Sources()
    {
        var src = Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj")))!;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(src, "XDrishti.Application", "Planning", "Loosenings"), "*.cs"))
        {
            yield return file;
        }

        foreach (var file in SliceFiles)
        {
            yield return Path.Combine(src, file);
        }
    }

    private static bool IsSliceType(Type t) => t.Name.StartsWith("Loosening", StringComparison.Ordinal) || t.Name is "DismissLooseningHandler" or "DismissLooseningCommand" or "ChangeLooseningSettingsHandler" or "ChangeLooseningSettingsCommand" or "GetLooseningSettingsHandler" or "GetLooseningSettingsQuery";

    [Fact]
    public void The_pure_rules_have_no_io_framework_clock_or_logging()
    {
        var result = Types.InAssembly(Domain).That().HaveNameStartingWith("Loosening")
            .ShouldNot().HaveDependencyOnAny("Npgsql", "Microsoft.EntityFrameworkCore", "System.Net.Http", "System.IO", "Microsoft.Extensions.Logging", "System.Threading.Tasks", "System.Diagnostics")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        foreach (var file in Sources().Where(f => f.Contains(Path.Combine("XDrishti.Domain", "Planning"), StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[] { "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }
        }
    }

    [Fact]
    public void No_type_of_the_slice_calls_a_limit_change_handler_the_plan_record_s_writers_or_the_builder()
    {
        var slice = Application.GetTypes().Where(IsSliceType).ToList();
        slice.Count.ShouldBeGreaterThan(8);

        foreach (var type in slice)
        {
            var result = Types.InAssembly(Application).That().HaveName(type.Name).ShouldNot().HaveDependencyOnAny(Writers).GetResult();
            result.IsSuccessful.ShouldBeTrue(type.FullName);
        }
    }

    [Fact]
    public void The_automatic_dismissal_reaches_only_the_dismissal_store_and_never_the_setting_s_writer()
    {
        var result = Types.InAssembly(Application).That().HaveName("LooseningAutoDismissal")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Planning.ChangeLooseningSettingsHandler", "XDrishti.Application.Planning.DismissLooseningHandler", "XDrishti.Application.Abstractions.Messaging.ICommandHandler`1", "XDrishti.Application.Abstractions.Messaging.ICommandHandler`2")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
        var ctor = Application.GetType("XDrishti.Application.Planning.LooseningAutoDismissal").ShouldNotBeNull().GetConstructors().Single();
        ctor.GetParameters().Select(p => p.ParameterType.Name).ShouldBe(["ILooseningStore", "IAccountEventLog", "IAccountUnitOfWork", "TimeProvider", "ILogger`1"]);
    }

    [Fact]
    public void The_plan_builder_and_the_plan_store_know_nothing_of_the_banner_so_it_can_never_reach_the_input_hash()
    {
        foreach (var name in new[] { "SelectionPlanBuilder", "NullPlanBuilder", "PlanBuildRequest" })
        {
            var result = Types.InAssembly(Application).That().HaveName(name)
                .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Abstractions.Planning.ILooseningStore", "XDrishti.Application.Planning.LooseningReader", "XDrishti.Application.Planning.LooseningView", "XDrishti.Domain.Planning.LooseningNotices", "XDrishti.Domain.Planning.LooseningNotice")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(name);
        }

        var store = Types.InAssembly(typeof(XDrishti.Infrastructure.DependencyInjection).Assembly).That().HaveName("PlanStore")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Abstractions.Planning.ILooseningStore", "XDrishti.Application.Planning.LooseningView", "XDrishti.Domain.Planning.LooseningNotices")
            .GetResult();
        store.IsSuccessful.ShouldBeTrue();
        typeof(XDrishti.Application.Abstractions.Planning.NewPlan).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Loosening", StringComparison.Ordinal));
    }

    [Fact]
    public void No_type_member_or_file_of_the_slice_names_an_order_upsize_leverage_margin_or_multiplier()
    {
        var types = Application.GetTypes().Concat(Domain.GetTypes()).Where(IsSliceType).ToList();
        foreach (var name in types.SelectMany(t => new[] { t.Name }.Concat(t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name))))
        {
            Banned().IsMatch(name).ShouldBeFalse(name);
            name.ShouldNotContain("Order", Case.Sensitive);
        }

        var files = Sources().ToList();
        files.Count.ShouldBeGreaterThan(10);
        foreach (var file in files)
        {
            Banned().IsMatch(File.ReadAllText(file)).ShouldBeFalse(file);
        }
    }

    [Fact]
    public void The_automatic_setting_is_stored_advanced_never_learnable_and_has_no_safer_direction()
    {
        var descriptor = SettingsRegistry.StoredSettings.Single(s => s.Key == "plans.loosening_banner.auto_dismiss");

        descriptor.Kind.ShouldBe(SettingKind.Stored);
        descriptor.Tier.ShouldBe(SettingTier.Advanced);
        descriptor.Safer.ShouldBe(SettingSafer.None);
        descriptor.Learnable.ShouldBeFalse();
        descriptor.Default.ShouldBe("false");
        LearnedAllowList.Keys.ShouldNotContain(descriptor.Key);
    }
}
