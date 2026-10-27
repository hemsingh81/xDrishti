using System.Text;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// The model port of the proposal job (spec P9-08 D1): one request over <see cref="IChatModel"/>, temperature 0 and a fixed seed, NO tools, the versioned system prompt and the pack in a fenced block as the only user message. The streamed parts are joined. A grammar-constrained JSON
/// format is the server's to offer (UNVERIFIED), so the answer is always re-read as data on our side. A failure of the server surfaces as <see cref="ModelUnavailableException"/>.
/// </summary>
internal sealed class ProposalModel(IChatModel model, TimeProvider time) : IProposalModel
{
    public async ValueTask<ProposalModelAnswer> ProposeAsync(ProposalModelRequest request, CancellationToken cancellationToken)
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

        return new ProposalModelAnswer(text.ToString(), promptTokens, completionTokens, finish, (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds));
    }
}
