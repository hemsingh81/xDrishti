namespace XDrishti.Application.Ai;

/// <summary>A question for the assistant. <paramref name="Origin"/> is <c>chat</c>, <c>eval</c> or <c>job</c> (stored on the conversation); all obey the same switches and refusals. <paramref name="Actor"/> is the owner (<c>owner:...</c>) or a service (<c>service:...</c>).</summary>
public sealed record TurnRequest(string Question, Guid? ConversationId, string Origin, string Actor)
{
    public const string Chat = "chat";
    public const string Eval = "eval";
    public const string Job = "job";
}
