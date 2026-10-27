namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>The codes of the checks C3 to C6 and of the shape of a model answer (spec P9-07 AC 3, 4, 5). A finding is a repair reason and is shown to the owner.</summary>
public static class DraftCodes
{
    public const string AssumptionNotInYaml = "assumption_not_in_yaml";
    public const string ExplanationNumberNotInSource = "explanation_number_not_in_source";
    public const string IdeaNumberDropped = "idea_number_dropped";
    public const string DeclineWithYaml = "decline_with_yaml";
    public const string YamlMissing = "yaml_missing";
    public const string HasVersion = "draft_has_version";
    public const string IdInvalid = "draft_id_invalid";
    public const string TooManyParams = "draft_too_many_params";
    public const string AnswerShape = "answer_shape";
    public const string YamlUnreadable = "yaml_unreadable";
}
