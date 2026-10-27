using XDrishti.Domain.Assistant;

namespace XDrishti.Application.Abstractions.Ai;

/// <summary>
/// The assistant's switches behind a port (spec P9-03, ADR 0137): <c>ai.settings</c>, insert-only for the runtime role. A change is a new row; the newest is current; with no row the assistant is off. Only an <see cref="AssistantSettingsChange"/> (owner actor only) can be appended.
/// </summary>
public interface IAssistantSettingsStore
{
    /// <summary>The settings in force: the newest row, or <see cref="AssistantSettings.Defaults"/> (off) when none.</summary>
    ValueTask<AssistantSettings> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>Appends the change if the newest row is still the one the caller saw (<paramref name="expectedCurrentId"/>, null for none); returns the new settings, or null when another change won.</summary>
    ValueTask<AssistantSettings?> AppendAsync(AssistantSettingsChange change, long? expectedCurrentId, CancellationToken cancellationToken);
}
