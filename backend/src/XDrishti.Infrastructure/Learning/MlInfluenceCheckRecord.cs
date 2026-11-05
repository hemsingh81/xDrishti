namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.ml_influence_checks</c> (spec CF-10 part 10c-2, ADR 0217): one evaluation of the influence predicate for a model. It exists for the model and the migration; the store writes it with SQL. Insert-only.</summary>
internal sealed class MlInfluenceCheckRecord
{
    public long Id { get; set; }

    public long ModelId { get; set; }

    public DateTimeOffset EvaluatedAt { get; set; }

    public bool MayInfluence { get; set; }

    public string Terms { get; set; } = "[]";

    public string SettingHash { get; set; } = string.Empty;

    public string InputsHash { get; set; } = string.Empty;
}
