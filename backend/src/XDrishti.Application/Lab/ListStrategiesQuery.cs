using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Application.Lab;

/// <summary>The families of the library with their latest version and counts (spec P5-01).</summary>
public sealed record ListStrategiesQuery : IQuery<IReadOnlyList<StoredStrategy>>;
