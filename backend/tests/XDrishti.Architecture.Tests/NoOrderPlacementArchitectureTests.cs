using System.Reflection;
using System.Text.RegularExpressions;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P7-03, ADR 0083, doc 19 §5: the system places no order of any kind until Phase 12. No type or member of the shipped assemblies may be named for placing, submitting, modifying or cancelling an order, in any casing or with underscores
/// (<c>PlaceOrder</c>, <c>submit_order</c>, <c>CancelOrderAsync</c>), or for sending, executing, buying or selling one; no API route may be an order route; no shipped assembly may reference a broker or order-routing library.
/// This is a tripwire, not a guarantee: it stops the obvious, not a determined bypass. The barrier is that no broker write endpoint is on the allow-list and no order-capable credential is held. Arming a ticket is a state in our own database and a
/// feed subscription, never an order. The rule is proved to fail on a planted example (<see cref="Planted"/>, a type of this test assembly only). Do not relax it: an order endpoint is a Phase 12 decision that starts with the owner.
/// </summary>
public sealed class NoOrderPlacementArchitectureTests
{
    private static readonly string[] Forbidden = ["placeorder", "submitorder", "modifyorder", "cancelorder", "placetrade", "submittrade", "canceltrade", "orderrequest", "neworder"];

    private static readonly string[] Shipped = ["XDrishti.Domain", "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "XDrishti.Feed", "XDrishti.Mcp"];

    private const BindingFlags AllMembers = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // An order verb on a broker-style noun. "Create" or "add" with "trade" is the owner's own trade journal and is not matched.
    private static readonly Regex Verbs = new("(create|send|post|execute|buy|sell|route|transmit)orders?|(execute|buy|sell|send|post|transmit)trades?(?!s?(batch|import|card|history))", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    // Route segments of the API that would be an order endpoint (plural "orders": the baskets reorder routes are singular "order").
    private static readonly Regex RouteSegments = new(@"(^|/)(orders|place-?orders?|send-to-broker|execute|square-?off|basket-orders?)(/|\{|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    // Libraries of brokers and order routing.
    private static readonly Regex BrokerLibraries = new(@"dhan|kite|zerodha|upstox|fyers|smartapi|angelone|alpaca|ibapi|interactivebrokers|binance|ccxt|tradier|5paisa|groww|quickfix|brokerapi", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>Test-only type used to prove the rule fails; it is never shipped.</summary>
    internal static class Planted
    {
        public static void PlaceOrder()
        {
        }
    }

    /// <summary>Test-only type whose name is the violation.</summary>
    internal sealed class Cancel_Order_Request;

    private static bool Denotes(string name)
    {
        var flat = name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return Forbidden.Any(f => flat.Contains(f, StringComparison.Ordinal)) || Verbs.IsMatch(flat);
    }

    /// <summary>Every type and member name of the assembly that denotes an order action.</summary>
    internal static List<string> Violations(Assembly assembly) =>
    [
        .. assembly.GetTypes().SelectMany(type =>
            (Denotes(type.Name) ? [type.FullName ?? type.Name] : Array.Empty<string>())
            .Concat(type.GetMembers(AllMembers).Where(m => Denotes(m.Name)).Select(m => $"{type.FullName}.{m.Name}"))),
    ];

    [Fact]
    public void No_shipped_type_or_member_places_modifies_or_cancels_an_order()
    {
        var found = Shipped.SelectMany(name => Violations(Assembly.Load(new AssemblyName(name)))).ToList();

        found.ShouldBeEmpty();
    }

    [Fact]
    public void The_scan_reads_the_shipped_assemblies()
    {
        Shipped.Sum(name => Assembly.Load(new AssemblyName(name)).GetTypes().Length).ShouldBeGreaterThan(500);
    }

    [Fact]
    public void The_rule_fails_on_a_planted_member_and_a_planted_type()
    {
        var found = Violations(typeof(NoOrderPlacementArchitectureTests).Assembly);

        found.ShouldContain(f => f.EndsWith("Planted.PlaceOrder", StringComparison.Ordinal));
        found.ShouldContain(f => f.EndsWith("Cancel_Order_Request", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("PlaceOrder", true)]
    [InlineData("submit_order", true)]
    [InlineData("MODIFYORDERAsync", true)]
    [InlineData("CancelOrder", true)]
    [InlineData("SendOrder", true)]
    [InlineData("ExecuteTrade", true)]
    [InlineData("create_order", true)]
    [InlineData("BuyTrade", true)]
    [InlineData("CreateTradeBatch", false)]
    [InlineData("AddTrade", false)]
    [InlineData("SellTradeCard", false)]
    [InlineData("PlaceTrade", true)]
    [InlineData("OrderRequest", true)]
    [InlineData("NewOrder", true)]
    [InlineData("OrderBook", false)]
    [InlineData("PlanItem", false)]
    [InlineData("ArmTicket", false)]
    public void Names_are_matched_in_any_casing_without_catching_the_trade_journal(string name, bool expected)
    {
        Denotes(name).ShouldBe(expected);
    }

    [Fact]
    public void No_api_route_is_an_order_route()
    {
        var anchor = RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "PlansEndpoints.cs");
        var routes = Directory.GetFiles(Path.GetDirectoryName(anchor)!, "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "Map(?:Get|Post|Put|Delete|Patch|Group)\\(\"([^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(5)).Select(m => (File: Path.GetFileName(f), Route: m.Groups[1].Value)))
            .ToList();

        routes.Count.ShouldBeGreaterThan(100);
        routes.Where(r => RouteSegments.IsMatch(r.Route)).Select(r => $"{r.File}: {r.Route}").ShouldBeEmpty();
        RouteSegments.IsMatch("/orders/{id}").ShouldBeTrue("the scan does see an order route");
        RouteSegments.IsMatch("/plans/{id}").ShouldBeFalse();
    }

    [Fact]
    public void No_shipped_assembly_references_a_broker_or_order_routing_library()
    {
        var references = Shipped
            .SelectMany(name => Assembly.Load(new AssemblyName(name)).GetReferencedAssemblies().Select(r => (Assembly: name, Reference: r.Name ?? string.Empty)))
            .Where(r => !r.Reference.StartsWith("XDrishti", StringComparison.Ordinal) && BrokerLibraries.IsMatch(r.Reference))
            .Select(r => $"{r.Assembly} -> {r.Reference}")
            .ToList();

        references.ShouldBeEmpty();
        BrokerLibraries.IsMatch("KiteConnect").ShouldBeTrue("the scan does see a broker library");
    }

    [Fact]
    public void No_later_migration_re_grants_the_plan_schemas_which_would_give_update_and_delete_back()
    {
        var snapshot = RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Persistence", "Migrations", "XDrishtiDbContextModelSnapshot.cs");
        var offenders = Directory.GetFiles(Path.GetDirectoryName(snapshot)!, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal) && !Path.GetFileName(f).Contains("AddPlanRecord", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), "GrantSchemaUsageToAppRole\\(\\s*(Schemas\\.(Trading|Workflow)|\"(trading|workflow)\")", RegexOptions.None, TimeSpan.FromSeconds(5)))
            .Select(f => Path.GetFileName(f))
            .ToList();

        offenders.ShouldBeEmpty();
    }
}
