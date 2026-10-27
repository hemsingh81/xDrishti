using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>Which model and runtime the eval is measuring, as the model server reports them (the port the assistant already has). A server that is not ready is <c>Known = false</c>: a run does not start and a status does not compare the model.</summary>
internal sealed class EvalModelEnvironment(ILlmHealth health) : IEvalEnvironment
{
    public async ValueTask<EvalModelInfo> ModelAsync(CancellationToken cancellationToken)
    {
        var status = await health.GetStatusAsync(cancellationToken);
        var ready = status.State == LlmState.Ready;
        return new EvalModelInfo(ready, status.State, status.ModelFile ?? status.Model ?? "unknown", status.ModelSha256 ?? string.Empty, status.Build ?? string.Empty);
    }
}
