namespace XDrishti.Domain.Assistant;

/// <summary>The hard limits of the tool contract (docs/specs/P9-00, spec P9-02). Constants, not options: a limit that is part of the contract is not tuned per deployment.</summary>
public static class ToolLimits
{
    /// <summary>Rows a tool result may hold; a tool may default lower.</summary>
    public const int MaxRows = 200;

    /// <summary>Default of a tool's own <c>maxRows</c> argument.</summary>
    public const int DefaultRows = 50;

    /// <summary>Characters of one cell; longer text is cut and ends with an ellipsis.</summary>
    public const int MaxCellChars = 300;

    /// <summary>Characters of the answer text handed to the model; rows are dropped from the end until it fits.</summary>
    public const int MaxModelChars = 6000;

    /// <summary>Bytes of a whole envelope, in any case.</summary>
    public const int MaxEnvelopeBytes = 64 * 1024;

    /// <summary>Characters of a <c>run_sql</c> query.</summary>
    public const int MaxSqlChars = 4000;

    /// <summary>Characters of a strategy text for <c>lab_validate</c>.</summary>
    public const int MaxYamlChars = 65536;

    /// <summary>Cuts a cell to <see cref="MaxCellChars"/> characters, the last one an ellipsis when something was cut.</summary>
    public static string CutCell(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= MaxCellChars ? text : string.Concat(text.AsSpan(0, MaxCellChars - 1), "…");
    }
}
