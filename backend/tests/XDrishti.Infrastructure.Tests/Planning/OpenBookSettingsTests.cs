using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Infrastructure.Planning;

namespace XDrishti.Infrastructure.Tests.Planning;

public sealed class OpenBookSettingsTests
{
    private static bool Read(string? value)
    {
        var data = new Dictionary<string, string?>();
        if (value is not null)
        {
            data["Planning:OpenBook:AssumedEmpty"] = value;
        }

        return new OpenBookSettings(new ConfigurationBuilder().AddInMemoryCollection(data).Build()).AssumedEmpty;
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("yes", false)]
    [InlineData("True", false)]
    [InlineData("TRUE", false)]
    [InlineData(" true", false)]
    [InlineData("true ", false)]
    [InlineData("1", false)]
    public void The_open_book_is_unavailable_unless_the_owner_sets_it_true(string? value, bool expected) =>
        Read(value).ShouldBe(expected);

    private static OpenBookSettings Settings(params (string Key, string Value)[] pairs) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value)).Build());

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", false)]
    [InlineData("1", false)]
    [InlineData("", false)]
    public void The_paper_ledger_is_off_unless_the_owner_sets_exactly_true(string value, bool expected) =>
        Settings(("Planning:OpenBook:UsePaperLedger", value)).UsePaperLedger.ShouldBe(expected);

    [Fact]
    public void Unset_both_switches_are_off()
    {
        var s = Settings();

        (s.AssumedEmpty, s.UsePaperLedger).ShouldBe((false, false));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void The_two_switches_are_exclusive_at_host_start(bool assumedEmpty, bool usePaper, bool valid) =>
        OpenBookCheck.IsValid(new OpenBookCheck { AssumedEmpty = assumedEmpty, UsePaperLedger = usePaper }).ShouldBe(valid);

    [Fact]
    public void Both_on_fails_validation_when_the_options_are_resolved()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Planning:OpenBook:AssumedEmpty"] = "true", ["Planning:OpenBook:UsePaperLedger"] = "true" }).Build());
        services.AddSingleton<IOpenBookSettings, OpenBookSettings>();
        services.AddOptions<OpenBookCheck>().Configure<IOpenBookSettings>((o, s) => (o.AssumedEmpty, o.UsePaperLedger) = (s.AssumedEmpty, s.UsePaperLedger)).Validate(OpenBookCheck.IsValid, OpenBookCheck.Message);

        var failure = Should.Throw<OptionsValidationException>(() => services.BuildServiceProvider().GetRequiredService<IOptions<OpenBookCheck>>().Value);

        failure.Message.ShouldContain("exclusive");
    }
}
