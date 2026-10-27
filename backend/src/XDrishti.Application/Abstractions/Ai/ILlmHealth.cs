namespace XDrishti.Application.Abstractions.Ai;

/// <summary>Asks the local model server whether it is up. Never throws for an unreachable server and is never called at start-up or by a health check (the API does not wait for the model).</summary>
public interface ILlmHealth
{
    Task<LlmStatus> GetStatusAsync(CancellationToken cancellationToken);
}
