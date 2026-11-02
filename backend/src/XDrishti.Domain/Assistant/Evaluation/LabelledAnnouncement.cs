namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>One row of <c>data/eval/sets/announcements.jsonl</c> (P15-07, the schema of <c>eval/sets/announcements.schema.json</c>): third-party text with the owner's label. Data only, never interpreted.</summary>
public sealed record LabelledAnnouncement(string Id, string Symbol, string PublishedAt, string Source, string Text, bool Material, string? Category);
