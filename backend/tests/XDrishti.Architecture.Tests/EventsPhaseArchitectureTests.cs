using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.LearningPhase;

namespace XDrishti.Architecture.Tests;

/// <summary>Spec P15-12 criterion 3: the P15 phase check only reads. The handler holds read ports and nothing else, the two ports it was given for the stores have one read member each, and the handler, the verb and the pure rule name no write store, setting change, model or order.</summary>
public sealed class EventsPhaseArchitectureTests
{
    private static readonly Type[] AllowedParameters =
    [
        typeof(IExtensionEvaluationReader), typeof(IEventFilterHistory), typeof(IFilingReader), typeof(IFilingInstruments), typeof(IResultsCalendar), typeof(IFilingSettings),
        typeof(IBarQueries), typeof(ISimulationEngineVersion), typeof(TimeProvider), typeof(Microsoft.Extensions.Logging.ILogger<>),
    ];

    private static readonly string[] WriteVerbs = ["Append", "Insert", "Save", "Write", "Store", "Set", "Apply", "Delete", "Update", "Remove", "Record", "Publish", "Place", "Submit", "Enqueue", "Start", "Commit", "Move", "Merge", "Upsert"];

    private static Type Handler() => typeof(EventsPhaseCheckQuery).Assembly.GetType("XDrishti.Application.LearningPhase.GetEventsPhaseCheckHandler", throwOnError: true)!;

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    [Fact]
    public void The_handler_takes_only_read_ports()
    {
        var parameters = Handler().GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        parameters.ShouldAllBe(t => AllowedParameters.Contains(t.IsGenericType ? t.GetGenericTypeDefinition() : t));
    }

    [Fact]
    public void Every_interface_the_handler_takes_has_no_write_member()
    {
        var interfaces = Handler().GetConstructors().Single().GetParameters().Select(p => p.ParameterType).Where(t => t.IsInterface && t.Namespace?.StartsWith("XDrishti", StringComparison.Ordinal) == true);

        foreach (var type in interfaces)
        {
            foreach (var method in type.GetInterfaces().Prepend(type).SelectMany(i => i.GetMethods()))
            {
                WriteVerbs.ShouldNotContain(v => method.Name.StartsWith(v, StringComparison.Ordinal), $"{type.Name}.{method.Name}");
            }
        }
    }

    [Fact]
    public void The_two_store_ports_have_one_read_member_each_and_are_not_the_write_stores()
    {
        typeof(IExtensionEvaluationReader).GetInterfaces().Prepend(typeof(IExtensionEvaluationReader)).SelectMany(i => i.GetMethods()).Select(m => m.Name).ShouldBe(["ListAsync"]);
        typeof(IEventFilterHistory).GetInterfaces().Prepend(typeof(IEventFilterHistory)).SelectMany(i => i.GetMethods()).Select(m => m.Name).ShouldBe(["RowsAsync"]);
        Handler().GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ShouldNotContain(typeof(IExtensionEvaluationStore));
        Handler().GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ShouldNotContain(typeof(IEventFilterDecisions));
    }

    [Fact]
    public void The_handler_verb_options_and_pure_rule_name_no_write_store_setting_model_or_order()
    {
        var files = new[]
        {
            Source("XDrishti.Application", "LearningPhase", "GetEventsPhaseCheckHandler.cs"),
            Source("XDrishti.Api", "Cli", "EventsPhaseCheckCommand.cs"),
            Source("XDrishti.Api", "Cli", "EventsPhaseCheckOptions.cs"),
            Source("XDrishti.Domain", "Learning", "Extensions", "EventsPhaseCheck.cs"),
            Source("XDrishti.Domain", "Learning", "Extensions", "EventsPhaseReport.cs"),
        };

        foreach (var text in files)
        {
            foreach (var name in new[] { "IExtensionEvaluationStore", "IEventFilterDecisions", "IEventFilterSwitch", "ICommandHandler", "ApplyEventFilterAuto", "AppendAsync", "AppendTrialAsync", "ITestLedger", "IAutonomySettingsStore", "ILlm", "IAssistant", "IOrder", "PlaceOrder", "File.Write", "INSERT ", "UPDATE ", "DELETE " })
            {
                text.ShouldNotContain(name);
            }
        }
    }
}
