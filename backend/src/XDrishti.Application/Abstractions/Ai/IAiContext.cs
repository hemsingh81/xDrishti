namespace XDrishti.Application.Abstractions.Ai;

/// <summary>Reads the assistant switches from the database itself (<c>ai.context_allows</c>), under the read-only role. One query, no cache: a switch turned off takes effect on the next call.</summary>
public interface IAiContext
{
    Task<AiContext> GetAsync(CancellationToken cancellationToken);
}
