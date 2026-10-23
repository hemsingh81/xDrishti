namespace XDrishti.Learning.MlNet;

/// <summary>The gradient-boosted-tree trainer behind a model (P6-06). The values are stored in the artifact header; do not renumber.</summary>
public enum TreeTrainer
{
    /// <summary>Microsoft.ML.LightGbm (native library).</summary>
    LightGbm = 1,

    /// <summary>Microsoft.ML.FastTree (managed trees with a small native helper).</summary>
    FastTree = 2,
}
