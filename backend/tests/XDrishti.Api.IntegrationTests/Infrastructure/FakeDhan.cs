using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Accounts;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The Dhan side of the data pipeline for tests: the source of intraday bars and the primary Data account. Without a fake set, the API's real source (over the fake
/// Dhan server, <see cref="FakeBrokers"/>) and the real account lookup answer; a test sets <see cref="Source"/> and/or <see cref="Account"/> and the
/// factory's <see cref="Reset"/> removes them again.
/// </summary>
public sealed class FakeDhan
{
    public IIntradayBarSource? Source { get; set; }

    public IDataAccountStatus? Account { get; set; }

    /// <summary>A source that is ready and gives each instrument the bars from <paramref name="bars"/> (called with the target).</summary>
    public FakeSource UseSource(Func<IntradayTarget, IntradayResult> bars)
    {
        var source = new FakeSource(bars);
        Source = source;
        return source;
    }

    /// <summary>A connected primary Data account; with <paramref name="accessToken"/> the real source can send it to the fake Dhan server (otherwise the account has none).</summary>
    public void UseConnectedAccount(string name = "Data account", string? accessToken = null) =>
        Account = new FakeAccount(new DataAccountState(Guid.NewGuid(), name, DataAccountStatuses.Connected, null), accessToken);

    public void UseAccount(DataAccountState state) => Account = new FakeAccount(state);

    public void Reset()
    {
        Source = null;
        Account = null;
    }

    /// <summary>Puts the switching wrappers in front of the real registrations (the real class is built by the container, so internal types stay internal).</summary>
    internal void Wrap(IServiceCollection services)
    {
        Wrap<IIntradayBarSource>(services, inner => new SwitchingSource(inner, this));
        Wrap<IDataAccountStatus>(services, inner => new SwitchingAccount(inner, this));
    }

    private static void Wrap<T>(IServiceCollection services, Func<T, T> wrap)
        where T : class
    {
        var original = services.Last(d => d.ServiceType == typeof(T));
        services.Remove(original);
        services.Add(new ServiceDescriptor(typeof(T), sp => wrap(Create<T>(sp, original)), original.Lifetime));
    }

    private static T Create<T>(IServiceProvider services, ServiceDescriptor original)
        where T : class =>
        original.ImplementationFactory is { } factory
            ? (T)factory(services)
            : (T)ActivatorUtilities.CreateInstance(services, original.ImplementationType ?? throw new InvalidOperationException($"{typeof(T).Name} is registered as an instance."));

    public sealed class FakeSource(Func<IntradayTarget, IntradayResult> bars) : IIntradayBarSource
    {
        public List<IntradayFetchPlan> Plans { get; } = [];

        public SourceAvailability Availability { get; set; } = SourceAvailability.Ready;

        public ValueTask<SourceAvailability> CheckAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Availability);

        public async IAsyncEnumerable<IntradayResult> FetchAsync(IntradayFetchPlan plan, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Plans.Add(plan);
            foreach (var target in plan.Targets)
            {
                await Task.Yield();
                var result = bars(target);
                yield return result;
                if (result.Failure?.Kind == IntradayFailureKind.Unauthorized)
                {
                    yield break;
                }
            }
        }
    }

    private sealed class FakeAccount(DataAccountState state, string? accessToken = null) : IDataAccountStatus
    {
        public ValueTask<DataAccountState> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(state);

        public ValueTask<SecretValue?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(SecretValue.TryCreate(accessToken, out var token, out _) ? token : null);
    }

    private sealed class SwitchingSource(IIntradayBarSource real, FakeDhan fake) : IIntradayBarSource
    {
        private IIntradayBarSource Current => fake.Source ?? real;

        public ValueTask<SourceAvailability> CheckAsync(CancellationToken cancellationToken) => Current.CheckAsync(cancellationToken);

        public IAsyncEnumerable<IntradayResult> FetchAsync(IntradayFetchPlan plan, CancellationToken cancellationToken) => Current.FetchAsync(plan, cancellationToken);
    }

    private sealed class SwitchingAccount(IDataAccountStatus real, FakeDhan fake) : IDataAccountStatus
    {
        private IDataAccountStatus Current => fake.Account ?? real;

        public ValueTask<DataAccountState> GetAsync(CancellationToken cancellationToken) => Current.GetAsync(cancellationToken);

        public ValueTask<SecretValue?> GetAccessTokenAsync(CancellationToken cancellationToken) => Current.GetAccessTokenAsync(cancellationToken);
    }
}
