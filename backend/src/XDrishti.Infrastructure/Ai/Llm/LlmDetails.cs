namespace XDrishti.Infrastructure.Ai.Llm;

/// <summary>The fixed vocabulary of <c>LlmStatus.Detail</c>: the only sentences the status can carry.</summary>
internal static class LlmDetails
{
    public const string SwitchedOff = "The assistant's model server is switched off in this deployment.";
    public const string NoAnswer = "No answer from the model server. Start it with scripts/xd-ai.sh start (it says model_missing when the model file is absent).";
    public const string Loading = "The model server is loading the model.";
    public const string Ready = "The model server is ready.";
    public const string KeyRejected = "The API key was rejected.";
    public const string KeyNotSet = "The API key is not set.";
    public const string OtherModel = "The server serves another model than the one expected.";
    public const string Unexpected = "The model server gave an answer that was not understood.";
}
