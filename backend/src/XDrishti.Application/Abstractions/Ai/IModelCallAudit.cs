namespace XDrishti.Application.Abstractions.Ai;

/// <summary>Records one model call that is not a chat turn into <c>ai.model_calls</c> (insert-only).</summary>
public interface IModelCallAudit
{
    ValueTask RecordAsync(ModelCallRecord record, CancellationToken cancellationToken);
}
