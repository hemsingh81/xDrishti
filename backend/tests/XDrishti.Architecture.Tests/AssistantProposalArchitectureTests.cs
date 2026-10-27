using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P9-08 AC 12 (ADR 0142): the model has no write path but the inbox. The generation job holds only the insert port and the read ports; only the applier names the hypothesis and basket commands and only the accept and auto handlers use the applier; no type of the proposal code
/// names a command that tracks an instrument, starts a backfill, arms a strategy or places an order; the model port is one adapter in <c>Infrastructure/Ai</c>.
/// </summary>
public sealed class AssistantProposalArchitectureTests
{
    private const string Proposals = "XDrishti.Application.Ai.Proposals";

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] JobParameters = ["ICommandHandler`2", "IAssistantSettingsStore", "ILlmHealth", "IModelCallAudit", "IProposalInbox", "IProposalModel", "IProposalReader", "ILogger`1", "ProposalEvaluator", "ProposalOptions", "ProposalPackBuilder", "TimeProvider", "TurnGate"];

    private static List<string> Users(string dependency) =>
        [.. Types.InAssembly(Application).That().ResideInNamespace(Proposals).And().HaveDependencyOn(dependency).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order()];

    [Fact]
    public void Only_the_applier_names_the_commands_that_write_a_hypothesis_or_change_a_basket()
    {
        foreach (var command in new[] { "XDrishti.Application.Learning.ProposeHypothesisCommand", "XDrishti.Application.Market.Baskets.AddBasketMembersCommand", "XDrishti.Application.Market.Baskets.RemoveBasketMembersCommand" })
        {
            Users(command).ShouldBe(["ProposalApplier"], command);
        }
    }

    [Fact]
    public void Only_the_accept_and_auto_handlers_use_the_applier_and_the_decision_port()
    {
        Users("XDrishti.Application.Ai.Proposals.ProposalApplier").ShouldBe(["AcceptProposalHandler", "AutoAcceptProposalsHandler", "ProposalApplier"]);
        Users(typeof(IProposalDecisions).FullName!).ShouldBe(["AcceptProposalHandler", "AutoAcceptProposalsHandler", "ProposalDecisionWriter", "RejectProposalHandler"]);
    }

    [Fact]
    public void The_generation_job_holds_the_inbox_insert_and_read_ports_and_may_only_ask_the_auto_option()
    {
        var job = Types.InAssembly(Application).That().HaveName("GenerateProposalsHandler").GetTypes().Single();

        job.GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).Order().ShouldBe(
            JobParameters.Order());
        var result = Types.InAssembly(Application).That().HaveName("GenerateProposalsHandler").Or().HaveName("ProposalPackBuilder").Or().HaveName("ProposalEvaluator")
            .ShouldNot().HaveDependencyOnAny(typeof(IProposalDecisions).FullName!, "XDrishti.Application.Ai.Proposals.ProposalApplier", "XDrishti.Application.Learning.ProposeHypothesisCommand", "XDrishti.Application.Market.Baskets", typeof(IChatModel).FullName!).GetResult();
        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Nothing_of_the_proposal_code_tracks_backfills_arms_or_orders()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace(Proposals)
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Application.Market.Tracking", "XDrishti.Application.Abstractions.Market.ITrackedInstrumentStore", "XDrishti.Application.Abstractions.Market.IMarketUnitOfWork", "XDrishti.Application.Abstractions.Live", "XDrishti.Application.Abstractions.Paper",
                "XDrishti.Application.Abstractions.Brokers", "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Lab", "XDrishti.Application.Planning").GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_model_port_is_one_adapter_and_the_inbox_ports_have_no_update_or_delete()
    {
        Types.InAssembly(Infrastructure).That().ImplementInterface(typeof(IProposalModel)).GetTypes().Select(t => t.Namespace).Distinct().ShouldBe(["XDrishti.Infrastructure.Ai.Assistant"]);
        typeof(IProposalInbox).GetMethods().Select(m => m.Name).ShouldBe(["InsertAsync"]);
        typeof(IProposalReader).GetMethods().Select(m => m.Name).Order().ShouldBe(["CountAutoBetweenAsync", "GetAsync", "ListAsync", "PendingAsync", "RejectedSinceAsync"]);
        typeof(IProposalDecisions).GetMethods().Select(m => m.Name).Order().ShouldBe(["InsertAsync", "LockAsync"]);
        typeof(IProposalModel).GetMethods().Select(m => m.Name).ShouldBe(["ProposeAsync"]);
        typeof(ProposalModelRequest).GetProperties().Select(p => p.Name).Order().ShouldBe(["MaxOutputTokens", "Seed", "System", "User"]);
    }
}
