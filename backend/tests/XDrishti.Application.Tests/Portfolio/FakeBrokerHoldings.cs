using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio;

/// <summary>A scripted Dhan holdings API: reports the configured rows (or the configured failure) and remembers every call's token.</summary>
internal sealed class FakeBrokerHoldings : IBrokerHoldings
{
    public List<BrokerHolding> Rows { get; } = [];

    public BrokerFailure? Failure { get; set; }

    public List<SecretValue> Tokens { get; } = [];

    public Func<Task>? BeforeAnswer { get; set; }

    public async ValueTask<BrokerResponse<IReadOnlyList<BrokerHolding>>> FetchAsync(SecretValue accessToken, CancellationToken cancellationToken)
    {
        Tokens.Add(accessToken);
        if (BeforeAnswer is { } wait)
        {
            await wait();
        }

        return Failure is { } failure ? BrokerResponse.Fail<IReadOnlyList<BrokerHolding>>(failure) : BrokerResponse.Ok<IReadOnlyList<BrokerHolding>>([.. Rows]);
    }
}

/// <summary>Maps rows to the instruments the test registered, by the row's key.</summary>
internal sealed class FakeInstrumentMapper : IInstrumentMapper
{
    public Dictionary<string, InstrumentRef> Known { get; } = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyDictionary<string, InstrumentRef>> MapAsync(IReadOnlyCollection<BrokerHolding> rows, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<string, InstrumentRef>>(rows.Where(r => Known.ContainsKey(r.Key)).ToDictionary(r => r.Key, r => Known[r.Key]));
}
