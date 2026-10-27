using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai;

/// <summary>
/// The only way to the model for question answering (spec P9-03, P9-00 D8): refusals first (<c>assistant.disabled</c>, invalid, not found, model or tools unavailable, nightly running, busy, too long; none stores anything), then a session that runs the tool loop. The chat endpoint streams the
/// session's events; the evaluation and later jobs call the extension RunAsync without HTTP, under the same switches.
/// </summary>
public interface IAssistantTurnRunner
{
    ValueTask<Result<ITurnSession>> BeginAsync(TurnRequest request, CancellationToken cancellationToken);
}
