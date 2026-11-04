namespace XDrishti.Domain.Options.Reports;

/// <summary>
/// The words of the P14 reports (spec P14-16, P8-00 D3): a figure is <c>ok</c> or <c>unavailable</c> with a reason from the closed list below, never a number in the place of a non-value; a verdict family is <c>stored</c>, <c>none stored</c> or <c>cut</c>.
/// </summary>
public static class ReportStates
{
    public const string Ok = "ok";
    public const string Unavailable = "unavailable";

    public const string Stored = "stored";
    public const string NoneStored = "none stored";
    public const string Cut = "cut";

    /// <summary>The closed list of reasons a report figure is unavailable.</summary>
    public static class Reasons
    {
        /// <summary>The archive holds no stored result or log row for it.</summary>
        public const string NotRecorded = "not_recorded";

        /// <summary>No half-spread model is stored for the underlying.</summary>
        public const string NoModel = "no_model";

        /// <summary>No run is stored.</summary>
        public const string NoRuns = "no_runs";

        /// <summary>The run paired no trade, or the formation selected no pair: the share or the distribution has no denominator.</summary>
        public const string NoPairs = "no_pairs";

        /// <summary>The formation's Benjamini-Hochberg test rejected nothing, so there is no threshold at the last rejection.</summary>
        public const string NoRejection = "no_rejection";
    }
}
