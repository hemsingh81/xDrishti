using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.File;

/// <summary>The strategy file (P3-02c, ADR 0055): reads untrusted YAML text into a <see cref="StrategyDefinition"/> with located problems, and writes a definition as YAML text. Pure and thread-safe.</summary>
public static class StrategyFile
{
    /// <summary>Reads the text. Never throws on any text; a refused text gives a result without a definition and with at least one error.</summary>
    public static FileReadResult Read(string text, FileLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var problems = new List<FileProblem>();
        var tree = FileTreeReader.Read(text.Length > 0 && text[0] == '﻿' ? text[1..] : text, limits ?? new FileLimits(), problems);
        if (tree is null)
        {
            return new FileReadResult(null, new FileEnvelope(null), problems, new Dictionary<string, FilePosition>());
        }

        var (definition, version, positions) = FileMapper.Map(tree, problems);
        return new FileReadResult(definition, new FileEnvelope(version), [.. problems.OrderBy(p => p.Line).ThenBy(p => p.Column)], positions);
    }

    /// <summary>Writes the definition in the fixed layout (P3-02c D7). <c>Read(Write(d))</c> equals <c>d</c>.</summary>
    public static string Write(StrategyDefinition definition) => FileWriter.Write(definition);

    /// <summary>The <c>sell:</c> block of a side as the file writes it, for the Lab to show a mirrored SELL the author can copy.</summary>
    public static string WriteSell(SideRules sell) => FileWriter.WriteSell(sell);
}
