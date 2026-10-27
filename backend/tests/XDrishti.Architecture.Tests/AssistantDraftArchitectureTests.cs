using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Lab;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P9-07 AC 6 (ADR 0141): the model has no write path. In <c>Application.Ai.Drafting</c> only the owner's confirm handler may name the commands that store a strategy and start a Lab run; the core and the job reach the model and the store only through their ports;
/// and the model port of a draft is the one adapter in <c>Infrastructure/Ai</c>. No other type of the Application layer outside the Lab names those commands for the assistant.
/// </summary>
public sealed class AssistantDraftArchitectureTests
{
    private const string Drafting = "XDrishti.Application.Ai.Drafting";

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Only_the_confirm_handler_references_the_commands_that_store_a_strategy_or_start_a_run()
    {
        foreach (var command in new[] { typeof(CreateStrategyCommand), typeof(StartLabRunCommand) })
        {
            var users = Types.InAssembly(Application).That().ResideInNamespace(Drafting).And().HaveDependencyOn(command.FullName!).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order().ToList();

            users.ShouldBe(["AutoDraftHandler", "ConfirmDraftHandler"], command.Name);
        }
    }

    [Fact]
    public void The_chat_tool_reaches_only_the_core_and_the_draft_store_never_the_library_the_lab_or_the_auto_option()
    {
        var result = Types.InAssembly(Application).That().HaveName("DraftProposalTool")
            .ShouldNot().HaveDependencyOnAny(
                typeof(CreateStrategyCommand).FullName!, typeof(StartLabRunCommand).FullName!, "XDrishti.Application.Abstractions.Research.IStrategyStore", "XDrishti.Application.Abstractions.Lab.ILabRunStore", "XDrishti.Application.Abstractions.Lab.IStrategyChecker",
                "XDrishti.Application.Ai.Drafting.ConfirmDraftHandler", "XDrishti.Application.Ai.Drafting.AutoDraftHandler", "XDrishti.Application.Ai.Drafting.AutoDraftCommand", "XDrishti.Application.Ai.Drafting.ConfirmDraftCommand", "XDrishti.Application.Abstractions.Market")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Application).That().HaveName("DraftProposalTool").GetTypes().Single().GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).Order().ShouldBe(["DraftOptions", "IDraftSheetSource", "IDraftStore", "IQueryHandler`2", "TimeProvider"]);
    }

    [Fact]
    public void Nothing_else_in_the_assistant_namespaces_references_those_commands()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Ai").And().DoNotResideInNamespace(Drafting)
            .ShouldNot().HaveDependencyOnAny(typeof(CreateStrategyCommand).FullName!, typeof(StartLabRunCommand).FullName!).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_job_and_the_core_reach_the_model_and_the_library_only_through_ports()
    {
        foreach (var type in new[] { "DraftStrategyHandler", "SubmitDraftHandler" })
        {
            var result = Types.InAssembly(Application).That().HaveName(type).ShouldNot().HaveDependencyOnAny(typeof(IChatModel).FullName!, "XDrishti.Application.Abstractions.Research.IStrategyStore", "XDrishti.Application.Abstractions.Live", "XDrishti.Application.Abstractions.Paper", "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Abstractions.Brokers").GetResult();

            result.IsSuccessful.ShouldBeTrue(type + " violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        Types.InAssembly(Infrastructure).That().ImplementInterface(typeof(IStrategyDrafter)).GetTypes().Select(t => t.Namespace).Distinct().ShouldBe(["XDrishti.Infrastructure.Ai.Assistant"]);
    }

    [Fact]
    public void The_draft_ports_have_no_update_or_delete_and_the_model_port_has_no_tools()
    {
        typeof(IDraftStore).GetMethods().Select(m => m.Name).Order().ShouldBe(["CountAutoBetweenAsync", "CountBetweenAsync", "DecisionAsync", "GetAsync", "InsertAsync", "InsertDecisionAsync", "ListAsync", "LockConfirmAsync", "ReserveIdAsync"]);
        typeof(IStrategyDrafter).GetMethods().Select(m => m.Name).ShouldBe(["DraftAsync"]);
        typeof(DraftModelRequest).GetProperties().Select(p => p.Name).Order().ShouldBe(["MaxOutputTokens", "Seed", "System", "User"]);
    }
}
