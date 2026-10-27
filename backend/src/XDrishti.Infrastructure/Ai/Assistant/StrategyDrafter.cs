using System.Text;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// The model port of the drafting job (spec P9-07 D2): one request over <see cref="IChatModel"/>, temperature 0 and a fixed seed, NO tools, the system prompt with the sheet and the user message with the idea in a fenced block. The streamed parts are joined. The answer is parsed as data by the
/// caller; a grammar-constrained JSON format is the server's to offer (UNVERIFIED), so the answer is always re-validated on our side. A failure of the server surfaces as <see cref="ModelUnavailableException"/>.
/// </summary>
internal sealed class StrategyDrafter(IChatModel model, TimeProvider time) : IStrategyDrafter
{
    public async ValueTask<DraftModelAnswer> DraftAsync(DraftModelRequest request, CancellationToken cancellationToken)
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

        return new DraftModelAnswer(text.ToString(), promptTokens, completionTokens, finish, (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds));
    }
}
