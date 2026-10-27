namespace XDrishti.Application.Abstractions.Ai;

/// <summary>The role of a message sent to the model.</summary>
public enum ModelRole
{
    System,
    User,
    Assistant,
    Tool,
}
