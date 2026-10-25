using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Architecture.Tests;

/// <summary>Spec P7-20 (ADR 0115): alerts and prompts only. The pure rules do no I/O, no alert code moves a ticket or reaches the plan record's writers, and the live-feed observer exists only when the setting says so.</summary>
public sealed class TicketAlertArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Planning.PreOpenRules).Assembly;

    private static readonly string[] PureRules =
    [
        "XDrishti.Domain.Planning.TicketCrossing",
        "XDrishti.Domain.Planning.CrossingKinds",
        "XDrishti.Domain.Planning.AlertAcks",
        "XDrishti.Domain.Planning.PromptRules",
    ];

    private static readonly string[] AlertCode =
    [
        "XDrishti.Application.Planning.TicketAlertObserver",
        "XDrishti.Application.Planning.TicketNoticeSource",
        "XDrishti.Application.Planning.TicketAlertReader",
        "XDrishti.Application.Planning.TicketPromptsService",
        "XDrishti.Application.Planning.GetPlanAlertsHandler",
        "XDrishti.Application.Planning.MuteTicketAlertsHandler",
        "XDrishti.Application.Planning.SnoozeTicketNoticeHandler",
        "XDrishti.Application.Planning.DismissTicketNoticeHandler",
    ];

    [Fact]
    public void The_pure_rules_have_no_io_framework_or_logging()
    {
        foreach (var name in PureRules)
        {
            var result = Types.InAssembly(Domain).That().HaveName(name.Split('.')[^1])
                .ShouldNot().HaveDependencyOnAny("Npgsql", "Microsoft.EntityFrameworkCore", "System.Net.Http", "System.IO", "Microsoft.Extensions.Logging")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(name);
        }
    }

    [Fact]
    public void No_alert_code_moves_a_ticket_writes_the_plan_record_or_touches_autonomy_or_a_broker()
    {
        foreach (var name in AlertCode)
        {
            var type = Application.GetType(name).ShouldNotBeNull(name);
            var result = Types.InAssembly(Application).That().HaveName(type.Name)
                .ShouldNot().HaveDependencyOnAny(
                    "XDrishti.Application.Planning.MoveTicketCommand",
                    "XDrishti.Application.Planning.MoveTicketHandler",
                    "XDrishti.Application.Planning.MovePlanCommand",
                    "XDrishti.Application.Abstractions.Planning.IPlanStore",
                    "XDrishti.Application.Abstractions.Planning.IAutoApprovalStore",
                    "XDrishti.Application.Abstractions.Autonomy",
                    "XDrishti.Application.Autonomy",
                    "XDrishti.Application.Abstractions.Brokers")
                .GetResult();

            if (type.Name != "GetPlanAlertsHandler") // the plan view only reads a plan to answer 404
            {
                result.IsSuccessful.ShouldBeTrue(name);
            }
        }
    }

    [Fact]
    public void The_store_port_has_no_way_to_change_a_ticket_or_remove_a_row()
    {
        var methods = typeof(XDrishti.Application.Abstractions.Planning.ITicketAlertStore).GetMethods().Select(m => m.Name).ToList();

        methods.ShouldNotContain(n => n.Contains("Move", StringComparison.Ordinal) || n.Contains("Delete", StringComparison.Ordinal) || n.Contains("Update", StringComparison.Ordinal) || n.Contains("Remove", StringComparison.Ordinal));
    }

    [Fact]
    public void The_observer_is_registered_only_when_the_setting_is_on()
    {
        var off = XDrishti.Application.DependencyInjection.AddTicketAlertObserver(new ServiceCollection(), false);
        var on = XDrishti.Application.DependencyInjection.AddTicketAlertObserver(new ServiceCollection(), true);

        off.Count(d => d.ServiceType == typeof(ILiveTickObserver)).ShouldBe(0);
        on.Count(d => d.ServiceType == typeof(ILiveTickObserver)).ShouldBe(1);
        Application.GetTypes().Count(t => typeof(ILiveTickObserver).IsAssignableFrom(t) && t.IsClass).ShouldBe(1);
    }

    [Fact]
    public void The_store_writes_only_its_own_three_tables_and_never_plan_events()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "XDrishti.Infrastructure", "Planning", "TicketAlertStore.cs")))
        {
            dir = dir.Parent;
        }

        var source = File.ReadAllText(Path.Combine(dir.ShouldNotBeNull().FullName, "src", "XDrishti.Infrastructure", "Planning", "TicketAlertStore.cs"));
        var writes = System.Text.RegularExpressions.Regex.Matches(source, @"(INSERT\s+INTO|UPDATE|DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?)\s+([a-z_]+\.[a-z_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))
            .Select(m => $"{m.Groups[1].Value.Split(' ')[0].ToUpperInvariant()} {m.Groups[2].Value}").Distinct().Order().ToList();

        writes.ShouldBe(["INSERT trading.ticket_alerts", "INSERT trading.ticket_notice_acks", "INSERT trading.ticket_prompts"]);
        source.ShouldNotContain("INTO trading.plan_events", Case.Insensitive);
    }
}
