using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.File;

/// <summary>The envelope of a file (P3-02c D2): what is not part of the strategy and not in its hash. <paramref name="Version"/> is the family's counter if the text carries it.</summary>
public sealed record FileEnvelope(int? Version);

/// <summary>
/// The result of reading a strategy file: the definition (null when there is an error), the envelope, every problem in file order and the position of each field by model path (so a compile problem at <c>buy.trigger[1]</c> can be shown
/// at its line). The reader does not compile: <c>StrategyCompiler</c> is the judge of the rules.
/// </summary>
public sealed record FileReadResult(StrategyDefinition? Definition, FileEnvelope Envelope, IReadOnlyList<FileProblem> Problems, IReadOnlyDictionary<string, FilePosition> Positions)
{
    /// <summary>Whether any problem is an error.</summary>
    public bool HasErrors => Problems.Any(p => p.Severity == ProblemSeverity.Error);

    /// <summary>Where the field at the model path is in the file, or null.</summary>
    public FilePosition? Locate(string path) => Positions.TryGetValue(path, out var position) ? position : null;
}
