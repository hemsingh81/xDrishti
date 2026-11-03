using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The code that may name a filings type or port for the event-risk filter and its loss tag (P15-06, ADR 0182), named here once so that the older rules of P15-01, P15-02 and P15-04 can say "nothing but the filings code, the wiring and these" and
/// <see cref="EventFilterArchitectureTests"/> can pin the exact sets. A new user is a deliberate edit of this file.
/// </summary>
internal static class EventFilterUsers
{
    /// <summary>Domain types outside <c>XDrishti.Domain.Filings</c> that name a filings type: the replay input carries the snapshot and the results-days inputs; the loss tagger takes the event instants; the two feature sets of P15-05 (and their inputs) read the filings visible at an instant; the filter observation of P15-10 carries the calendar's answer and classifies it with the one window rule.</summary>
    public static readonly string[] DomainTypes = ["EventFeatureInput", "EventFeatures", "FilterObservation", "FundamentalFeatureInput", "FundamentalFeatures", "InstrumentReplayInput", "LossTagAnalyzer", "LossTagger"];

    /// <summary>Simulation types that read the snapshot and call the pure filter: the simulator and its comparison (the replay only hands the input's snapshot on); <c>ResultsDaysScan</c> turns the results-days inputs into the frame's per-date series (P15-05).</summary>
    public static readonly string[] SimulationTypes = ["ResultsDaysScan", "Simulator", "SimulatorComparison"];

    /// <summary>Application types outside the filings namespaces that name a filings type or port, by area: the run, the plan, the switch, the loss tag; P15-08: the set-D reader and runner of the eval (they hold the category baseline and the sample file).</summary>
    public static readonly string[] ApplicationTypes =
    [
        "ApplyEventFilterAutoHandler", "ChangeEventFilterHandler", "GetEventFilterHandler", "ExplainBasis", "InstrumentInputReader", "RunSimulationHandler", "RunContext", "PlanEventFacts", "PlanEventFilter", "SelectionPlanBuilder", "AnalyzeLossTagsHandler", "LossTagEvents", "ILossTagEvents", "ExtractExtensionFeaturesHandler", "ExtensionFeatureExtractor", "ExtensionSources", "FilterPopulationSource", "ComparisonEvaluation", "SurpriseInputsReader", "ISurpriseInputsReader", "AnnouncementSetLoad", "AnnouncementSetRunner", "GetEventsPhaseCheckHandler",
    ];

    /// <summary>A name pattern that matches the given roots, the compiler-generated types (<c>&lt;...&gt;</c>) and every user above.</summary>
    public static string WithRoots(string roots) => "^(" + roots + "|<.*|" + string.Join('|', [.. DomainTypes, .. SimulationTypes, .. ApplicationTypes]) + ")$";

    /// <summary>A name pattern that matches only the given names and the compiler-generated types.</summary>
    public static string Only(string[] names) => "^(<.*|" + string.Join('|', names) + ")$";

    /// <summary>The names (nested types folded into their outer type's root name, compiler-generated types left out) of the types of the assembly outside the given namespaces that have a dependency on any of the filings namespaces.</summary>
    public static List<string> UsersIn(Assembly assembly, string[] filingsNamespaces, params string[] ownNamespaces)
    {
        var types = Types.InAssembly(assembly).That().HaveDependencyOnAny(filingsNamespaces).GetTypes();
        return
        [
            .. types
                .Where(t => !ownNamespaces.Any(n => (t.Namespace ?? string.Empty).StartsWith(n, StringComparison.Ordinal)))
                .Select(t => t.Name)
                .Where(n => !n.StartsWith('<'))
                .Distinct()
                .Order(StringComparer.Ordinal),
        ];
    }
}
