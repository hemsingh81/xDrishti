using Microsoft.Extensions.Time.Testing;
using XDrishti.Infrastructure.Portfolio;

namespace XDrishti.Infrastructure.Tests.Portfolio;

/// <summary>The 60-second cache of portfolio histories.</summary>
public sealed class PortfolioHistoryCacheTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));

    private PortfolioHistoryCache Cache() => new(_clock);

    [Fact]
    public void A_stored_value_is_returned_until_sixty_seconds_have_passed()
    {
        var cache = Cache();
        var value = new object();
        cache.Set("k", value, cache.Version);

        _clock.Advance(TimeSpan.FromSeconds(59));
        cache.TryGet<object>("k", out var fresh).ShouldBeTrue();
        _clock.Advance(TimeSpan.FromSeconds(1));
        cache.TryGet<object>("k", out var expired).ShouldBeFalse();

        fresh.ShouldBeSameAs(value);
        expired.ShouldBeNull();
    }

    [Fact]
    public void An_unknown_key_or_a_value_of_another_type_is_a_miss()
    {
        var cache = Cache();
        cache.Set("k", "text", cache.Version);

        cache.TryGet<object>("other", out _).ShouldBeFalse();
        cache.TryGet<Uri>("k", out _).ShouldBeFalse();
    }

    [Fact]
    public void Invalidate_forgets_everything_and_changes_the_version()
    {
        var cache = Cache();
        var before = cache.Version;
        cache.Set("a", new object(), before);
        cache.Set("b", new object(), before);

        cache.Invalidate();

        cache.TryGet<object>("a", out _).ShouldBeFalse();
        cache.TryGet<object>("b", out _).ShouldBeFalse();
        cache.Version.ShouldNotBe(before);
    }

    [Fact]
    public void A_value_computed_before_an_invalidation_is_not_stored()
    {
        var cache = Cache();
        var read = cache.Version;
        cache.Invalidate();

        cache.Set("k", new object(), read);

        cache.TryGet<object>("k", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_cache_keeps_at_most_the_limit_dropping_the_oldest()
    {
        var cache = Cache();
        for (var i = 0; i < PortfolioHistoryCache.MaxEntries + 10; i++)
        {
            cache.Set("k" + i, new object(), cache.Version);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        cache.TryGet<object>("k0", out _).ShouldBeFalse();
        cache.TryGet<object>("k" + (PortfolioHistoryCache.MaxEntries + 9), out _).ShouldBeTrue();
    }

    [Fact]
    public void Concurrent_use_is_safe()
    {
        var cache = Cache();

        Parallel.For(0, 2000, i =>
        {
            cache.Set("k" + (i % 100), new object(), cache.Version);
            cache.TryGet<object>("k" + (i % 100), out _);
            if (i % 250 == 0)
            {
                cache.Invalidate();
            }
        });

        cache.Version.ShouldBeGreaterThan(0);
    }
}
