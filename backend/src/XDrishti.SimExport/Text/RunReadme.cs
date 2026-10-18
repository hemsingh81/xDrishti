using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Text;

/// <summary>
/// <c>README.txt</c> (spec P3-04b1, E5): the banner, then a paragraph saying what the folder is and that the CSV files must not be passed on without this notice. A CSV is what gets opened in a spreadsheet and sent on, and it carries no
/// label itself (a label would break its columns), so this sidecar sits in the same folder and is hashed in the manifest. No time, no path.
/// </summary>
public static class RunReadme
{
    /// <summary>The text of the readme for the run folder <paramref name="runId"/>.</summary>
    public static string Text(RunLabelSet labels, string runId)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        return RunBanner.Text(labels)
            + "\n"
            + "This folder (" + runId + ") is the output of the xDrishti simulate verb: a run of one strategy over past data.\n"
            + "\n"
            + "  trades.csv       the simulated trades, one row each, with the run key in every row\n"
            + "  open_at_end.csv  the trades the data cut short, apart on purpose\n"
            + "  run.json         the full record of the run: strategy, rules, data facts, counters and trades\n"
            + "  manifest.json    the size and SHA-256 of every other file, and the export key\n"
            + "\n"
            + "Nothing in this folder is evidence of an edge. The labels above say what the numbers are; read them before the trades.\n"
            + "Do not pass trades.csv or open_at_end.csv on without this notice: a CSV file has no room for it.\n";
    }
}
