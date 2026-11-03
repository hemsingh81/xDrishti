namespace XDrishti.Domain.Options.Costs;

/// <summary>A model ready to store: its content address, the underlying, the sessions it rests on, the settings it was built with (as text) and its buckets.</summary>
public sealed record BuiltSpreadModel(string ModelId, Guid UnderlyingId, int Sessions, DateOnly FirstSession, DateOnly LastSession, string SettingsJson, IReadOnlyList<SpreadBucketRow> Buckets);
