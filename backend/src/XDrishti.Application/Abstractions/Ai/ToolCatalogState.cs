namespace XDrishti.Application.Abstractions.Ai;

/// <summary>Whether the tool server can be used: <c>Ready</c> (listed), <c>Down</c> (configured but not answering) or <c>Disabled</c> (no address or token configured).</summary>
public enum ToolCatalogState
{
    Ready,
    Down,
    Disabled,
}
