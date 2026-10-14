using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The SignalR hub of <c>xd-api</c> (ADR 0024 §7, P2-10a): the hub and its host code are thin and depend on the Application's ports; the push logic knows no database, socket or web framework; the API never
/// references the feed process; no method of the hub can be called without the owner's session.
/// </summary>
public sealed class LiveHubArchitectureTests
{
    private static readonly Assembly Api = typeof(Program).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static Type ApiType(string name) => Api.GetType("XDrishti.Api.Live." + name).ShouldNotBeNull();

    [Fact]
    public void The_api_does_not_reference_the_feed_or_the_worker()
    {
        var referenced = Api.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referenced.ShouldNotContain("XDrishti.Feed");
        referenced.ShouldNotContain("XDrishti.Worker");
    }

    [Fact]
    public void The_hub_and_its_host_code_depend_on_the_applications_ports_only()
    {
        var result = Types.InAssembly(Api).That().ResideInNamespace("XDrishti.Api.Live")
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Infrastructure", "XDrishti.Feed", "XDrishti.Worker", "XDrishti.Domain", "Microsoft.EntityFrameworkCore", "Npgsql",
                "XDrishti.Application.Live.Push.LivePushCoordinator", "XDrishti.Application.Live.Push.PushConnection")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_hub_reaches_the_push_only_through_the_subscriptions_port()
    {
        var parameters = ApiType("EventsHub").GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        parameters.ShouldContain(typeof(ILiveSubscriptions));
        parameters.ShouldNotContain(t => t.Namespace != null && (t.Namespace.StartsWith("XDrishti.Infrastructure", StringComparison.Ordinal) || t.Namespace.StartsWith("XDrishti.Application.Live", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_push_logic_knows_no_database_socket_or_web_framework()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Live.Push")
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Infrastructure", "XDrishti.Api", "XDrishti.Feed", "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "System.Net.WebSockets", "System.Net.Http")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_hub_requires_a_session_and_no_method_of_it_is_anonymous()
    {
        var hub = ApiType("EventsHub");

        hub.GetCustomAttributes<AuthorizeAttribute>().ShouldNotBeEmpty();
        hub.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        hub.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ShouldAllBe(m => !m.GetCustomAttributes<AllowAnonymousAttribute>().Any());
    }

    [Fact]
    public void The_hubs_server_methods_are_the_two_of_the_contract()
    {
        var methods = ApiType("EventsHub").GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is not ("OnConnectedAsync" or "OnDisconnectedAsync"))
            .Select(m => m.Name);

        methods.ShouldBe(["Subscribe", "Unsubscribe"], ignoreOrder: true);
    }

    [Fact]
    public void The_ports_are_implemented_by_the_layers_that_own_the_data_and_the_coordinator_is_internal()
    {
        Infrastructure.GetType("XDrishti.Infrastructure.Platform.LivePriceChanges").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(ILivePriceChanges));
        Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.LivePriceReader").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(ILivePriceReader));
        Infrastructure.GetType("XDrishti.Infrastructure.Portfolio.LiveScopeResolver").ShouldNotBeNull().GetInterfaces().ShouldContain(typeof(ILiveScopeResolver));
        ApiType("SignalRLiveClients").GetInterfaces().ShouldContain(typeof(ILiveClients));

        var coordinator = Application.GetType("XDrishti.Application.Live.Push.LivePushCoordinator").ShouldNotBeNull();
        coordinator.IsNotPublic.ShouldBeTrue();
        coordinator.IsSealed.ShouldBeTrue();
        coordinator.GetInterfaces().ShouldContain(typeof(ILiveSubscriptions));
        coordinator.GetInterfaces().ShouldContain(typeof(ILivePushLoop));
    }
}
