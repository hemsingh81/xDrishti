namespace XDrishti.Infrastructure.Ai.ReadOnly.DataReader;

/// <summary>The five kinds <c>get_learning</c> reads: the column list and the status column are constants of this file, never text from the model (the model's status is a parameter).</summary>
internal sealed record LearningKind(string View, IReadOnlyList<LearningColumn> Columns, string Sql);

internal sealed record LearningColumn(string Name, bool IsText);

internal static class LearningKinds
{
    public static LearningKind? Find(string kind) => kind switch
    {
        "lessons" => Build("lessons", "state", "created_at DESC", ("lesson_id", false), ("state", false), ("monitor_kind", false), ("subject", true), ("problem", true), ("result", true), ("experiment_outcome", false), ("before_mean_r", false), ("after_mean_r", false), ("created_at", false)),
        "hypotheses" => Build("hypotheses", "state", "created_at DESC", ("hypothesis_id", false), ("state", false), ("source", false), ("cell_key", false), ("knob_name", false), ("baseline_value", false), ("candidate_value", false), ("rationale", true), ("created_at", false)),
        "changelog" => Build("learning_changelog", "to_state", "created_at DESC", ("kind", false), ("subject_kind", false), ("subject", true), ("from_state", false), ("to_state", false), ("reason", true), ("created_at", false)),
        "experiments" => Build("experiments", "outcome", "created_at DESC", ("experiment_key", false), ("knob_name", false), ("baseline_value", false), ("candidate_value", false), ("outcome", false), ("trials", false), ("used_folds", false), ("folds_better", false), ("folds_needed", false), ("baseline_trades", false), ("candidate_trades", false), ("pooled_baseline_mean_r", false), ("pooled_candidate_mean_r", false), ("created_at", false)),
        "models" => Models(),
        _ => null,
    };

    private static LearningKind Build(string view, string statusColumn, string order, params (string Name, bool IsText)[] columns) =>
        new("aiv." + view, [.. columns.Select(c => new LearningColumn(c.Name, c.IsText))], $"SELECT {string.Join(", ", columns.Select(c => c.Name))} FROM aiv.{view} WHERE (@status::text IS NULL OR {statusColumn} = @status) ORDER BY {order} LIMIT @n");

    private static LearningKind Models()
    {
        (string, bool)[] columns = [("version_key", false), ("trainer", false), ("feature_version", false), ("verdict", false), ("fit_rows", false), ("from_date", false), ("to_date", false), ("pooled_beats_both", false), ("created_at", false)];
        var list = string.Join(", ", columns.Select(c => "m." + c.Item1));
        return new LearningKind(
            "aiv.model_versions",
            [.. columns.Select(c => new LearningColumn(c.Item1, c.Item2)), new LearningColumn("role", false)],
            $"SELECT {list}, (SELECT d.role FROM aiv.model_designations d WHERE d.version_key = m.version_key ORDER BY d.created_at DESC LIMIT 1) AS role FROM aiv.model_versions m WHERE (@status::text IS NULL OR m.verdict = @status) ORDER BY m.created_at DESC LIMIT @n");
    }
}
