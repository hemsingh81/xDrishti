using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts.Connections;

/// <summary>A scripted Dhan: answers as configured (by default: success for the made-up holder) and remembers every call.</summary>
internal sealed class FakeConnector : IBrokerConnector
{
    public const string Marker = AccountsFixture.Marker;

    /// <summary>The holder Dhan reports by default: the fixture's client id.</summary>
    public string HolderId { get; set; } = AccountsFixture.ClientId;

    public string? HolderName { get; set; } = "Example Holder";

    public DateTimeOffset? StatedExpiry { get; set; }

    public BrokerFailure? StartFailure { get; set; }

    public BrokerFailure? CompleteFailure { get; set; }

    public List<LoginStartRequest> Starts { get; } = [];

    public List<LoginCompletion> Completions { get; } = [];

    public static SecretValue Secret(string text) => SecretValue.TryCreate(text, out var value, out _) ? value : throw new ArgumentException("invalid", nameof(text));

    public ValueTask<BrokerResponse<Uri>> StartLoginAsync(LoginStartRequest request, CancellationToken cancellationToken)
    {
        Starts.Add(request);
        return ValueTask.FromResult(StartFailure is { } failure
            ? BrokerResponse.Fail<Uri>(failure)
            : BrokerResponse.Ok(new Uri($"https://login.example.invalid/dhan?n={Starts.Count}")));
    }

    public ValueTask<BrokerResponse<BrokerToken>> CompleteLoginAsync(LoginCompletion completion, CancellationToken cancellationToken)
    {
        Completions.Add(completion);
        return ValueTask.FromResult(CompleteFailure is { } failure
            ? BrokerResponse.Fail<BrokerToken>(failure)
            : BrokerResponse.Ok(new BrokerToken(Secret($"access-{Completions.Count}-{Marker}"), HolderId, HolderName, StatedExpiry)));
    }
}
