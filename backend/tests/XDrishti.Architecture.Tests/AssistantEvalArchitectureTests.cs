using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P9-09 AC 13 and 15 (ADR 0143): the eval can only read the fixture and only the owner's verb starts it or accepts a run. The eval code holds no write port of the owner's data (settings, strategies, hypotheses, baskets, drafts, proposals, briefings), never reaches the confirm,
/// auto or accept paths, and never the production tool catalog; nothing but the verb names the run and accept commands; the store has no update or delete; the pure part is pure.
/// </summary>
public sealed class AssistantEvalArchitectureTests
{
    private const string Eval = "XDrishti.Application.Assistant.Evaluation";

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static List<string> Users(Assembly assembly, string dependency) =>
        [.. Types.InAssembly(assembly).That().HaveDependencyOn(dependency).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order()];

    [Fact]
    public void The_eval_code_reaches_no_write_path_of_the_owners_data_and_no_owner_act()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace(Eval)
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Application.Ai.ChangeAssistantSettingsCommand", "XDrishti.Application.Ai.Drafting.ConfirmDraftHandler", "XDrishti.Application.Ai.Drafting.AutoDraftCommand", "XDrishti.Application.Ai.Drafting.DecisionWriter", "XDrishti.Application.Ai.Drafting.StartDraftCommand",
                "XDrishti.Application.Ai.Drafting.DraftStrategyCommand", typeof(IDraftStore).FullName!, "XDrishti.Application.Ai.Proposals", typeof(IProposalInbox).FullName!, typeof(IProposalDecisions).FullName!, typeof(IBriefingStore).FullName!,
                "XDrishti.Application.Lab", "XDrishti.Application.Learning", "XDrishti.Application.Market", "XDrishti.Application.Planning", "XDrishti.Application.Paper", "XDrishti.Application.Abstractions.Brokers", "XDrishti.Application.Abstractions.Accounts",
                "XDrishti.Application.Abstractions.Live", "XDrishti.Application.Abstractions.Paper", "XDrishti.Application.Auth").GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_eval_code_never_names_the_production_tool_catalog_and_the_eval_turn_is_always_origin_eval()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace(Eval).ShouldNot().HaveDependencyOnAny(typeof(IToolCatalog).FullName!, "XDrishti.Application.Ai.AssistantTurnService").GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        Users(Application, typeof(IEvalToolCatalog).FullName!).ShouldBe(["EvalTurnRunnerFactory"], "only the factory builds the eval turn, over the eval catalog");
        Users(Infrastructure, typeof(IEvalToolCatalog).FullName!).ShouldBe(["AssistantRegistration", "EvalFixtureProbe", "EvalToolCatalog"]);
    }

    [Fact]
    public void Only_the_owners_verb_names_the_run_the_grade_and_the_accept_commands()
    {
        Users(Application, "XDrishti.Application.Assistant.Evaluation.RunEvalCommand").ShouldBe(["DependencyInjection", "RunEvalCommand", "RunEvalHandler"]);
        Users(Application, "XDrishti.Application.Assistant.Evaluation.AcceptEvalCommand").ShouldBe(["AcceptEvalCommand", "AcceptEvalHandler", "DependencyInjection"]);
        Users(Api, "XDrishti.Application.Assistant.Evaluation.RunEvalCommand").ShouldBe(["EvalCommand"]);
        Users(Api, "XDrishti.Application.Assistant.Evaluation.AcceptEvalCommand").ShouldBe(["EvalCommand"]);
        Users(Api, "XDrishti.Application.Assistant.Evaluation.GradeEvalAnswersCommand").ShouldBe(["EvalCommand"]);
        Users(Api, "XDrishti.Infrastructure.Ai.Evaluation.FixtureSeeder").ShouldBe(["EvalCommand"]);
    }

    [Fact]
    public void The_eval_endpoints_can_only_read()
    {
        var result = Types.InAssembly(Api).That().HaveName("AssistantEvalEndpoints")
            .ShouldNot().HaveDependencyOnAny(typeof(XDrishti.Application.Abstractions.Messaging.ICommandHandler<>).FullName!, "XDrishti.Application.Assistant.Evaluation.RunEvalCommand", "XDrishti.Application.Assistant.Evaluation.AcceptEvalCommand", "XDrishti.Application.Assistant.Evaluation.GradeEvalAnswersCommand").GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_eval_store_has_no_update_or_delete_and_the_answer_source_is_one_method()
    {
        typeof(IEvalStore).GetMethods().Select(m => m.Name).Order().ShouldBe(["BaselineOfAsync", "CasesAsync", "CurrentBaselineAsync", "GetAsync", "InsertBaselineAsync", "InsertRunAsync", "ListAsync", "NewestGatingAsync"]);
        typeof(IEvalAnswerSource).GetMethods().Select(m => m.Name).ShouldBe(["AskAsync"]);
        typeof(IEvalFixtureProbe).GetMethods().Select(m => m.Name).ShouldBe(["ReadFixtureHashAsync"]);
    }

    [Fact]
    public void The_graders_and_the_statistics_are_pure()
    {
        var result = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Assistant.Evaluation")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application", "XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql", "System.Net.Http", "System.IO.File", "System.Random").GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_seeder_writes_only_through_a_connection_it_is_given_and_no_service_or_endpoint_names_it()
    {
        Users(Infrastructure, "XDrishti.Infrastructure.Ai.Evaluation.FixtureSeeder").ShouldBe(["FixtureSeeder"]);
        typeof(XDrishti.Infrastructure.Ai.Evaluation.FixtureSeeder).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name).ShouldBe(["SeedAsync"]);
    }
}
