namespace XDrishti.Application.Abstractions.Lab;

/// <summary>
/// The strategy file library as the Lab reaches it (spec P5-01, ADR 0057): a pure, synchronous port in front of the file reader and the compiler, which live in engine modules Application must not name. No I/O; never throws on any text.
/// </summary>
public interface IStrategyChecker
{
    /// <summary>Reads and compiles the text on every declared timeframe and returns every problem; the description is present when none is an error.</summary>
    StrategyCheck Check(string text);

    /// <summary>The text with its strategy id replaced, in the fixed layout of the file; null when the text does not read.</summary>
    string? Retarget(string text, string newId);

    /// <summary>The JSON Schema of the strategy file.</summary>
    string SchemaJson();
}
