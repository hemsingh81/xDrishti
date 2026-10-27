using System.Text.Json.Serialization;

namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>How one set of the eval ended against its thresholds.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SetState>))]
public enum SetState
{
    /// <summary>Every threshold of the set holds.</summary>
    Pass,

    /// <summary>A threshold of the set fails.</summary>
    Fail,

    /// <summary>The set was not run (or not all of its cases were): it can neither pass nor fail.</summary>
    NotRun,

    /// <summary>There are too few labelled rows to say (events before P15); shown, never blocking.</summary>
    NotEvaluable,
}
