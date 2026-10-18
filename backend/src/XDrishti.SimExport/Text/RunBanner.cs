using System.Text;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Text;

/// <summary>
/// The banner of a run in the two parts a renderer needs (spec P3-04b1, E5): <see cref="Visible"/>, the labels that are always shown, one per line, and <see cref="Limits"/>, what the fills do not model, one per line as
/// <c>not modelled: code: text</c>. <see cref="Text"/> is both, for the console and the readme. Every line ends in a line feed, so the parts concatenate to the whole. Nothing here is HTML: the cards wrap <see cref="Visible"/> in an
/// always-shown block and <see cref="Limits"/> in a <c>&lt;details&gt;</c>.
/// </summary>
public static class RunBanner
{
    /// <summary>The labels, one per line.</summary>
    public static string Visible(RunLabelSet labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var text = new StringBuilder();
        foreach (var line in labels.Lines)
        {
            text.Append(line).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>What the fills do not model, one <c>not modelled: code: text</c> per line.</summary>
    public static string Limits(RunLabelSet labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var text = new StringBuilder();
        foreach (var limit in labels.NotModelled)
        {
            text.Append("not modelled: ").Append(limit.Code).Append(": ").Append(limit.Text).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The whole banner: <see cref="Visible"/> then <see cref="Limits"/>.</summary>
    public static string Text(RunLabelSet labels) => Visible(labels) + Limits(labels);
}
