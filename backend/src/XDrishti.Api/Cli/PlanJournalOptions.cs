namespace XDrishti.Api.Cli;

/// <summary>The parsed command line of <c>xd plan journal</c> (spec P7-14): only an optional date, read like the date of <c>xd plan grade</c>.</summary>
internal sealed record PlanJournalOptions(DateOnly? Date)
{
    public const string Usage = """
        usage:
          xd plan journal [--date YYYY-MM-DD]
            matches the fills the portfolio sync already stored to the approved tickets of a plan by account, instrument, side and day, tags the rest discretionary,
            and grades finished round trips in R and price slippage against the plan; fill grades are labelled 'realised fills, not simulated'
            a fill that fits more than one ticket is listed and left unmatched; nothing is guessed, nothing is changed once written
            without --date: the newest 30 plans up to today
            fills are only read; no order is placed, nothing is approved and no ticket changes state
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>Parses the arguments after <c>plan journal</c>; the errors are listed when the options are null.</summary>
    public static (PlanJournalOptions? Options, IReadOnlyList<string> Errors) Parse(IReadOnlyList<string> args)
    {
        var (grade, errors) = PlanGradeOptions.Parse(args);
        return grade is null ? (null, errors) : (new PlanJournalOptions(grade.Date), errors);
    }
}
