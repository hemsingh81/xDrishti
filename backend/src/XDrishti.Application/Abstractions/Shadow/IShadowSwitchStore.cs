namespace XDrishti.Application.Abstractions.Shadow;

/// <summary>The owner's stored switch <c>paper.shadow.enabled</c> (spec CF-08, D7; ADR 0212): append only. The value the services read is <see cref="IShadowSettings"/>. No row means OFF.</summary>
public interface IShadowSwitchStore
{
    /// <summary>Appends a row (the database stamps its id in commit order and its time) and returns the id.</summary>
    ValueTask<long> AppendAsync(bool enabled, string actor, string reason, CancellationToken cancellationToken);
}
