namespace XDrishti.Domain.Planning;

/// <summary>The ids of the gates of the nightly pipeline, in the order they are read (spec P7-08; part of the stored <c>plan_runs.failed_gate</c>: never rename one), and the ids of the stages after them.</summary>
public static class NightlyGateIds
{
    public const string Calendar = "calendar";

    public const string Eod = "eod";

    public const string PortfolioSync = "portfolio-sync";

    public const string FreshData = "fresh-data";

    public const string Quality = "data-quality";

    public const string SelectionInputs = "selection-inputs";

    /// <summary>Not a gate: the stage after them. A builder that refuses or fails is recorded under this id and publishes nothing.</summary>
    public const string Builder = "plan-builder";

    /// <summary>The stage that stores the plan; an error there is recorded under this id.</summary>
    public const string Publish = "publish";
}
