using XDrishti.Application.Abstractions.Market.Bars;

namespace XDrishti.Application.Tests.Live;

/// <summary>Credentials that count how often they are read (the gates must keep this at zero).</summary>
internal sealed class CountingCredentials : IDataAccountCredentials
{
    private readonly FakeCredentials _inner = new();

    public int Reads { get; private set; }

    public string? Token
    {
        get => _inner.Token;
        set => _inner.Token = value;
    }

    public ValueTask<DataAccountCredentials?> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return _inner.GetCredentialsAsync(cancellationToken);
    }
}
