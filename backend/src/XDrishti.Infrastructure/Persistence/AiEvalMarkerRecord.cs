namespace XDrishti.Infrastructure.Persistence;

/// <summary><c>ai.eval_marker</c> (spec P9-09 AC 13): a one-row table the fixture seeder writes last, with the hash of the fixture file. Empty in every other database, so a probe of the production database finds no marker. The runtime role has no right on it; the tool role reads it through a view.</summary>
internal sealed class AiEvalMarkerRecord
{
    public short Id { get; set; }

    public string FixtureHash { get; set; } = string.Empty;

    public DateTimeOffset SeededAt { get; set; }
}
