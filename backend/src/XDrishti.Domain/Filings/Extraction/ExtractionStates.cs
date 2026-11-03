namespace XDrishti.Domain.Filings.Extraction;

/// <summary>The four states of a stored extraction (P15-08 criterion 4) and the closed reasons of the three that are not <c>ok</c>. A reason is a word of at most 30 characters, never text from the model or the announcement.</summary>
public static class ExtractionStates
{
    public const string Ok = "ok";
    public const string InvalidOutput = "invalid_output";
    public const string ModelUnavailable = "model_unavailable";
    public const string TooLong = "too_long";

    public static IReadOnlyList<string> All { get; } = [Ok, InvalidOutput, ModelUnavailable, TooLong];

    // Reasons of invalid_output.
    public const string Empty = "empty";
    public const string BadEncoding = "bad_encoding";
    public const string NotJson = "not_json";
    public const string NotOneObject = "not_one_object";
    public const string BadKeys = "bad_keys";
    public const string BadValue = "bad_value";
    public const string TooLongReason = "reason_too_long";
    public const string ToolCall = "tool_call";
    public const string CutShort = "cut_short";

    // Reasons of the other two states.
    public const string ServerFailed = "server_failed";
    public const string TextTooLong = "text_too_long";
}
