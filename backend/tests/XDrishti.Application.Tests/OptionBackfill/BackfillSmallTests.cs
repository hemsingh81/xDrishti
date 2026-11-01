using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.OptionBackfill;
using static XDrishti.Application.Tests.OptionBackfill.BackfillSamples;

namespace XDrishti.Application.Tests.OptionBackfill;

/// <summary>The pure small parts: the night window, the outcome words, the key state and the probe (which stores nothing).</summary>
public sealed class BackfillSmallTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(16, 29, false)]
    [InlineData(16, 30, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]
    [InlineData(8, 29, true)]
    [InlineData(8, 30, false)]
    [InlineData(12, 0, false)]
    public void The_window_is_1630_to_0830_india_time_with_the_end_exclusive(int hour, int minute, bool open)
    {
        BackfillWindow.IsOpen(At(12, hour, minute)).ShouldBe(open);
        BackfillWindow.IsOpen(At(12, hour, minute).ToUniversalTime()).ShouldBe(open, "the zone of the instant does not matter");
    }

    [Theory]
    [InlineData(12, 17, 0, 12)]
    [InlineData(12, 23, 59, 12)]
    [InlineData(13, 0, 5, 12)]
    [InlineData(13, 8, 29, 12)]
    [InlineData(13, 8, 30, 13)]
    [InlineData(13, 12, 0, 13)]
    [InlineData(13, 16, 30, 13)]
    public void The_night_is_keyed_to_the_india_date_of_the_window_start(int day, int hour, int minute, int night)
    {
        BackfillWindow.NightOf(At(day, hour, minute)).ShouldBe(new DateOnly(2026, 10, night));
    }

    [Fact]
    public void The_day_start_is_midnight_india_time()
    {
        BackfillWindow.DayStart(At(13, 5, 0)).ShouldBe(At(13, 0, 0));
        BackfillWindow.DayStart(At(12, 23, 59)).ShouldBe(At(12, 0, 0));
    }

    [Theory]
    [InlineData(200, null, BackfillOutcomes.Ok)]
    [InlineData(400, "DH-907", BackfillOutcomes.NoData)]
    [InlineData(200, "DH-907", BackfillOutcomes.NoData)]
    [InlineData(429, null, BackfillOutcomes.RateLimited)]
    [InlineData(400, "DH-904", BackfillOutcomes.RateLimited)]
    [InlineData(400, "DH-905", BackfillOutcomes.BadRequest)]
    [InlineData(401, null, BackfillOutcomes.TokenRefused)]
    [InlineData(403, null, BackfillOutcomes.TokenRefused)]
    [InlineData(400, "DH-901", BackfillOutcomes.TokenRefused)]
    [InlineData(400, "807", BackfillOutcomes.TokenRefused)]
    [InlineData(400, "809", BackfillOutcomes.TokenRefused)]
    [InlineData(400, "DH-902", BackfillOutcomes.NotSubscribed)]
    [InlineData(400, "806", BackfillOutcomes.NotSubscribed)]
    [InlineData(500, null, BackfillOutcomes.ServerError)]
    [InlineData(400, "DH-908", BackfillOutcomes.ServerError)]
    [InlineData(400, "DH-909", BackfillOutcomes.ServerError)]
    [InlineData(404, null, BackfillOutcomes.ServerError)]
    public void Dhans_answer_maps_to_one_outcome_word(int status, string? code, string expected)
    {
        BackfillOutcomeMap.From(status, code).ShouldBe(expected);
    }

    [Fact]
    public void Only_ok_empty_and_no_data_finish_a_key_and_four_words_count_as_failed_attempts()
    {
        BackfillOutcomes.All.Where(BackfillOutcomes.IsDone).ShouldBe(["ok", "empty", "no_data"]);
        BackfillOutcomes.All.Where(BackfillOutcomes.IsFailedAttempt).ShouldBe(["timeout", "server_error", "bad_body"], "a rate limit, bad_request and store_failed are not failed attempts of the key");
        new KeyState(3, 3, null).IsGivenUp.ShouldBeTrue();
        new KeyState(4, 3, "ok").IsGivenUp.ShouldBeFalse("a key that finished is not given up");
        new KeyState(2, 2, null).IsGivenUp.ShouldBeFalse();
        new KeyState(3, 0, null, 3).IsGivenUp.ShouldBeTrue("three answers that could not be stored");
        new KeyState(3, 0, null, 2).IsGivenUp.ShouldBeFalse();
    }

    [Fact]
    public async Task The_probe_makes_at_most_four_requests_prints_what_came_back_and_has_no_store()
    {
        var clock = new Platform.Services.SettableClock(At(12, 20, 0));
        var client = new FakeBackfillClient(clock);
        var probe = new BackfillProbe(client, Small);

        var lines = await probe.RunAsync([Nifty, BankNifty, Stock], At(12, 20, 0), Ct);

        client.Calls.Count.ShouldBe(BackfillProbe.MaxRequests);
        client.Calls.Select(c => (c.Request.SecurityId, c.Request.ExpiryFlag, c.Request.ExpiryCode, c.Request.Offset, c.Request.IntervalMinutes)).ShouldBe([(13L, 'W', 1, 10, 5), (13L, 'W', 2, 10, 5), (25L, 'W', 1, 0, 5), (25L, 'W', 1, 0, 5), (2885L, 'M', 1, 3, 15)]);
        client.Calls[2].Request.From.ShouldBe(new DateOnly(2024, 6, 1));
        client.Calls[3].Request.From.ShouldBe(new DateOnly(2025, 1, 1), "a window after BANKNIFTY's weekly era");
        client.Calls.ShouldAllBe(c => c.Request.Side == "CE");
        var text = string.Join('\n', lines);
        text.ShouldContain("UNVERIFIED");
        text.ShouldContain("kept=3");
        text.ShouldContain("09:15 India time");
        text.ShouldContain("iv as sent");
        text.ShouldNotContain("https");
        text.ShouldNotContain("token", Case.Insensitive);
        typeof(BackfillProbe).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ShouldBe([typeof(IOptionBackfillClient), typeof(OptionBackfillSettings)], "the probe holds no store");
    }

    [Fact]
    public async Task The_probe_stops_at_a_refused_token_and_skips_a_missing_underlying()
    {
        var clock = new Platform.Services.SettableClock(At(12, 20, 0));
        var client = new FakeBackfillClient(clock) { Responder = (_, _) => FakeBackfillClient.Failed(BackfillOutcomes.TokenRefused, 401, "DH-901") };

        var lines = await new BackfillProbe(client, Small).RunAsync([Nifty], At(12, 20, 0), Ct);

        client.Calls.Count.ShouldBe(1);
        string.Join('\n', lines).ShouldContain("Dhan refused the token");

        var none = await new BackfillProbe(new FakeBackfillClient(clock), Small).RunAsync([], At(12, 20, 0), Ct);
        none.Count(l => l.Contains("skipped", StringComparison.Ordinal)).ShouldBe(BackfillProbe.MaxRequests);
    }
}
