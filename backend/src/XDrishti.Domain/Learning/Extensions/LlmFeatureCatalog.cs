namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The extension <c>llm-1</c> (P15-08, ADR 0189): the columns the LLM announcement flag would add to a trade's features, in the order they would be stored. DEFINITIONS ONLY: nothing extracts them in this slice and <see cref="ExtensionCatalogs.GroupOf"/> does not return the group,
/// so no feature row can be written. P15-10 builds the extraction, and only after protocol L has passed (the newest accepted eval run has set D PASS with the stored model and prompt hashes).
/// </summary>
public static class LlmFeatureCatalog
{
    public const string Extension = "llm";

    public const string Version = "llm-1";

    /// <summary>Count of announcements flagged material in the 5 sessions before the decision, as broadcast by the decision instant.</summary>
    public const string MaterialCount5d = "llm_material_5d";

    /// <summary>Whether a rating downgrade was extracted in the 30 days before the decision (1 or 0).</summary>
    public const string Downgrade30d = "llm_rating_downgrade_30d";

    /// <summary>Whether guidance was raised (1), lowered (-1) or neither (0) in the 30 days before the decision.</summary>
    public const string GuidanceDirection30d = "llm_guidance_dir_30d";

    public static readonly IReadOnlyList<string> Names = [MaterialCount5d, Downgrade30d, GuidanceDirection30d];

    public static ExtensionGroup Group { get; } = new(Extension, Version, Names);
}
