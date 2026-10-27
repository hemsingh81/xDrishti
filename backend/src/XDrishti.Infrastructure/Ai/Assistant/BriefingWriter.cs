using System.Text;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// The model port of the briefing (spec P9-06): one non-streaming-looking request over <see cref="IChatModel"/> (the streamed parts are joined), temperature 0 and a fixed seed, no tools, the versioned system prompt and the facts block as the only user message. Nothing is called until a
/// briefing asks. A failure of the server surfaces as <see cref="ModelUnavailableException"/>; cancelling closes the connection.
/// </summary>
internal sealed class BriefingWriter(IChatModel model, TimeProvider time) : IBriefingWriter
{
    public async ValueTask<BriefingWrite> WriteAsync(BriefingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = time.GetTimestamp();
        var body = new ModelRequest([new ModelMessage(ModelRole.System, request.System), new ModelMessage(ModelRole.User, request.User)], [], request.MaxOutputTokens, 0f, request.Seed);
        var text = new StringBuilder();
        var finish = "stop";
        int? promptTokens = null;
        int? completionTokens = null;
        await foreach (var part in model.StreamAsync(body, cancellationToken))
        {
            switch (part)
            {
                case ModelTextPart t:
                    text.Append(t.Text);
                    break;
                case ModelFinishPart f:
                    finish = f.Reason;
                    promptTokens = f.PromptTokens;
                    completionTokens = f.CompletionTokens;
                    break;
                default:
                    break;
            }
        }

        return new BriefingWrite(text.ToString(), promptTokens, completionTokens, finish, (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds));
    }
}
