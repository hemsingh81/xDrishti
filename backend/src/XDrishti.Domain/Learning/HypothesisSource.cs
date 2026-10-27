namespace XDrishti.Domain.Learning;

/// <summary>Where a hypothesis came from (spec P6-10). The assistant source (P9-08) is a hypothesis the owner accepted from the assistant's inbox.</summary>
public enum HypothesisSource
{
    /// <summary>The owner wrote it.</summary>
    Owner,

    /// <summary>A value from the deterministic grid around the current value (<see cref="HypothesisGrid"/>).</summary>
    Grid,

    /// <summary>The local model proposed it and the owner (or the auto option) accepted the proposal (P9-08); it is still only a proposed hypothesis. Stored as the code <c>model</c> (the column is 8 characters wide and a view depends on it).</summary>
    Assistant,
}
