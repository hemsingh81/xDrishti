namespace XDrishti.Domain.Assistant.Evaluation;

/// <summary>The summary of a run: the verdict, the sets, and the sentence about the small sample. A run with one repeat is <see cref="NonGating"/> and can never be accepted.</summary>
public sealed record RunSummary(string Verdict, bool NonGating, IReadOnlyList<SetSummary> Sets, string SmallSampleLine)
{
    public SetSummary? Find(string set) => Sets.FirstOrDefault(s => s.Set == set);

    /// <summary>The count that the owner compares between runs: data passes, plans repaired passes, ideas repaired valid.</summary>
    public int? PrimaryPasses(string set) => Find(set)?.Rates.FirstOrDefault(r => r.Name == PrimaryRate(set))?.K;

    public static string PrimaryRate(string set) => set switch { "data" => "pass", "plans" => "repaired_pass", "ideas" => "repaired_valid", _ => "events" };
}
