using System.Reflection;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P7-03, ADR 0083, doc 19 §5: the system places no order of any kind until Phase 12. No type or member of the shipped assemblies may be named for placing, submitting, modifying or cancelling an order, in any casing or with underscores
/// (<c>PlaceOrder</c>, <c>submit_order</c>, <c>CancelOrderAsync</c>). Arming a ticket is a state in our own database and a feed subscription, never an order. The rule is proved to fail on a planted example (<see cref="Planted"/>, a type of this
/// test assembly only). Do not relax it: an order endpoint is a Phase 12 decision that starts with the owner, the SEBI algo steps and a change of this rule.
/// </summary>
public sealed class NoOrderPlacementArchitectureTests
{
    private static readonly string[] Forbidden = ["placeorder", "submitorder", "modifyorder", "cancelorder"];

    private static readonly string[] Shipped = ["XDrishti.Domain", "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "XDrishti.Feed"];

    private const BindingFlags AllMembers = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

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
        return Forbidden.Any(f => flat.Contains(f, StringComparison.Ordinal));
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
    [InlineData("OrderBook", false)]
    [InlineData("PlanItem", false)]
    [InlineData("ArmTicket", false)]
    public void Names_are_matched_in_any_casing(string name, bool expected)
    {
        Denotes(name).ShouldBe(expected);
    }
}
