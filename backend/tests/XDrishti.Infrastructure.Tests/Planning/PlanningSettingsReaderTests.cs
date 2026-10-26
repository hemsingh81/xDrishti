using Microsoft.Extensions.Configuration;
using XDrishti.Application.Planning;
using XDrishti.Domain.Paper;
using XDrishti.Infrastructure.Paper;

namespace XDrishti.Infrastructure.Tests.Planning;

/// <summary>Spec P10-05 criterion 3: the flattened Planning section holds the measured settings and no notification choice.</summary>
public sealed class PlanningSettingsReaderTests
{
    private static PlanningSettingsReader Reader(Dictionary<string, string?> values) => new(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public void Every_option_of_the_ticket_alerts_is_left_out_so_toggling_them_never_changes_the_fingerprint()
    {
        var alertKeys = typeof(TicketAlertOptions).GetProperties().ToDictionary(p => TicketAlertOptions.SectionName + ":" + p.Name, _ => (string?)"1");
        var measured = new Dictionary<string, string?> { ["Planning:Grading:Capital"] = "100000", ["Planning:Builder:UseSelection"] = "false" };

        var without = Reader(measured).Read();
        var with = Reader(measured.Concat(alertKeys).ToDictionary(p => p.Key, p => p.Value)).Read();

        alertKeys.ShouldNotBeEmpty();
        with.ShouldBe(without);
        ConfigFingerprint.Create(with.Select(p => new ConfigComponent("planning." + p.Key, p.Value))).Hash
            .ShouldBe(ConfigFingerprint.Create(without.Select(p => new ConfigComponent("planning." + p.Key, p.Value))).Hash);
    }

    [Fact]
    public void The_measured_planning_settings_are_read_lower_cased()
    {
        var read = Reader(new Dictionary<string, string?> { ["Planning:Grading:Capital"] = "100000", ["Planning:OpenBook:AssumedEmpty"] = "true" }).Read();

        read.Count.ShouldBe(2);
        read["grading:capital"].ShouldBe("100000");
        read["openbook:assumedempty"].ShouldBe("true");
    }
}
