using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Ai;

/// <summary>Maps the port's answer to the view. Reads only; it starts and stops nothing (the owner starts the server with <see cref="Command"/>).</summary>
internal sealed class GetLlmStatusHandler(ILlmHealth health) : IQueryHandler<GetLlmStatusQuery, LlmStatusView>
{
    public const string Command = "scripts/xd-ai.sh start";

    public async ValueTask<LlmStatusView> HandleAsync(GetLlmStatusQuery query, CancellationToken cancellationToken)
    {
        var status = await health.GetStatusAsync(cancellationToken);
        return new LlmStatusView(
            status.State.ToString().ToLowerInvariant(), status.Model, status.ModelFile, status.ModelSha256, status.Build, status.CheckedAt, status.Detail, Command);
    }
}
