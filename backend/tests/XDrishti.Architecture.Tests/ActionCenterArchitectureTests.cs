using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Quotes;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The Action center and the price alerts (ADR 0022, P2-09): the rules are pure — each one a function over a read model, with no I/O, clock or logging and no port — the use cases reach the decisions, the thresholds and the alerts only through ports that the
/// infrastructure implements, and the quote poll reaches the alerts only through the alert port (by way of the checker), never through the alert store, the alert use cases or the database.
/// </summary>
public sealed class ActionCenterArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] ForbiddenInRules =
    [
        "XDrishti.Infrastructure", "XDrishti.Api", "XDrishti.Hosting", "XDrishti.Worker",
        "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "Microsoft.AspNetCore", "Npgsql", "Serilog",
        "System.Net.Http", "System.IO", "System.Data",
        "System.TimeProvider", "System.Security.Cryptography.RandomNumberGenerator",
        "XDrishti.Application.Abstractions.Messaging", "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Abstractions.Market",
        typeof(IActionStates).FullName!, typeof(IActionSettings).FullName!, typeof(IPriceAlerts).FullName!, typeof(IPriceAlertStore).FullName!,
        typeof(IHoldingQueries).FullName!, typeof(IHoldingClassifications).FullName!, typeof(IPortfolioPrices).FullName!, typeof(IPortfolioBenchmark).FullName!, typeof(IPortfolioUnitOfWork).FullName!,
        "XDrishti.Application.Portfolio.Actions.ActionEvaluator", "XDrishti.Application.Portfolio.Actions.ActionReconciler", "XDrishti.Application.Portfolio.Actions.ActionDecisions",
    ];

    private static readonly string[] RuleNames =
        ["BelowCostRule", "LargeGainRule", "OverweightRule", "SectorConcentrationRule", "LongTermSoonRule", "LaggingNiftyRule", "NotInBasketRule", "ActionRuleSet", "ItemFactory", "ActionText"];

    private static Type Named(string name) => Application.GetTypes().Single(t => t.Name == name);

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void The_rules_depend_on_nothing_that_does_io_tells_the_time_logs_or_reaches_a_port()
    {
        var types = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Portfolio.Actions.Rules");

        types.GetTypes().ShouldNotBeEmpty();
        ShouldHaveNone(types.ShouldNot().HaveDependencyOnAny(ForbiddenInRules).GetResult());
    }

    [Fact]
    public void Each_rule_is_a_static_function_with_no_state_of_its_own()
    {
        var rules = RuleNames.Select(Named).ToList();

        rules.ShouldAllBe(t => t.IsAbstract && t.IsSealed);
        rules.ShouldAllBe(t => t.GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(f => f.IsLiteral || f.IsInitOnly));
    }

    [Fact]
    public void The_domain_value_objects_of_the_action_center_do_not_depend_on_the_application()
    {
        var result = Types.InAssembly(typeof(XDrishti.Domain.Common.Result).Assembly).That().HaveNameStartingWith("Action").Or().HaveNameStartingWith("PriceAlert").Or().HaveNameStartingWith("Alert")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application", "XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();

        ShouldHaveNone(result);
    }

    [Fact]
    public void The_use_cases_are_sealed_handlers_that_are_not_public()
    {
        var handlers = Application.GetTypes()
            .Where(t => t.Namespace is "XDrishti.Application.Portfolio.Actions" or "XDrishti.Application.Portfolio.Alerts" && t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Append(Named("GetPortfolioNoticesHandler"))
            .ToList();

        handlers.Count.ShouldBeGreaterThanOrEqualTo(10);
        handlers.ShouldAllBe(t => t.IsSealed && !t.IsPublic);
    }

    [Fact]
    public void The_use_cases_reach_the_database_only_through_ports()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Portfolio.Actions").Or().ResideInNamespace("XDrishti.Application.Portfolio.Alerts")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "XDrishti.Api")
            .GetResult();

        ShouldHaveNone(result);
    }

    [Fact]
    public void The_ports_are_implemented_in_the_infrastructure()
    {
        typeof(IActionStates).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.ActionStates")).ShouldBeTrue();
        typeof(IActionSettings).IsAssignableFrom(Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.ActionSettings")).ShouldBeTrue();
        var alerts = Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.PriceAlertStore");
        typeof(IPriceAlerts).IsAssignableFrom(alerts).ShouldBeTrue();
        typeof(IPriceAlertStore).IsAssignableFrom(alerts).ShouldBeTrue();
    }

    [Fact]
    public void The_quote_poll_reaches_the_alerts_only_through_the_checker_and_the_checker_only_through_the_alert_port()
    {
        var service = Types.InAssembly(Application).That().HaveName(nameof(HoldingQuotesService))
            .ShouldNot().HaveDependencyOnAny(
                typeof(IPriceAlertStore).FullName!, "XDrishti.Application.Portfolio.Alerts.CreatePriceAlertHandler", "XDrishti.Application.Portfolio.Alerts.CancelPriceAlertHandler",
                "XDrishti.Application.Portfolio.Alerts.ListPriceAlertsHandler", "XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();
        var checker = Types.InAssembly(Application).That().HaveName("PriceAlertChecker")
            .ShouldNot().HaveDependencyOnAny(typeof(IPriceAlertStore).FullName!, typeof(IHoldingQuotes).FullName!, "XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        ShouldHaveNone(service);
        ShouldHaveNone(checker);
        Named("PriceAlertChecker").GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType.Name).ShouldContain(nameof(IPriceAlerts));
    }

    [Fact]
    public void The_quote_poll_does_not_know_the_decisions_only_the_reconciler()
    {
        var result = Types.InAssembly(Application).That().HaveName(nameof(HoldingQuotesService))
            .ShouldNot().HaveDependencyOnAny(typeof(IActionStates).FullName!, typeof(IActionSettings).FullName!, "XDrishti.Application.Portfolio.Actions.ActionEvaluator")
            .GetResult();

        ShouldHaveNone(result);
    }
}
