namespace XDrishti.Domain.Baskets.Derived;

/// <summary>How members are chosen: the best k by rank, every instrument passing a gate (optionally capped), or clusters.</summary>
public enum SelectionForm { TopK, Threshold, Clusters }
