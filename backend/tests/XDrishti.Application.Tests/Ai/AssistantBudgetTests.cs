using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Ai;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-03 AC 5: the size rules of a prompt, the budget of tool calls and the registered prompt (its clauses by keyword, at most 900 tokens, a stable hash).</summary>
public sealed class AssistantBudgetTests
{
    [Fact]
    public void A_token_is_estimated_as_characters_over_three_and_a_half()
    {
        ContextBudget.EstimateTokens(string.Empty).ShouldBe(0);
        ContextBudget.EstimateTokens(new string('a', 35)).ShouldBe(10);
        ContextBudget.EstimateTokens(new string('a', 36)).ShouldBe(11);
    }

    [Fact]
    public void The_schema_estimate_counts_name_description_and_schema()
    {
        var tools = new[] { new ToolDescriptor("get_report", new string('d', 70), new string('s', 350)) };

        ContextBudget.SchemaTokens(tools).ShouldBe(ContextBudget.EstimateTokens("get_report") + 20 + 100);
    }

    [Fact]
    public void History_drops_the_oldest_messages_first_when_over_its_budget_and_keeps_the_newest()
    {
        var stored = Enumerable.Range(1, 6).Select(i => new HistoryMessage(i % 2 == 1 ? "user" : "assistant", $"m{i} " + new string('x', 1000), [])).ToList();

        var kept = ContextBudget.History(stored, 1500);

        kept.Count.ShouldBeLessThan(6);
        kept[^1].Content.ShouldStartWith("m6");
        kept.Sum(m => ContextBudget.EstimateTokens(m.Content)).ShouldBeLessThanOrEqualTo(1500);
    }

    [Fact]
    public void An_assistant_message_carries_one_line_for_its_calls_with_long_arguments_cut_and_never_the_results()
    {
        var calls = new[] { new HistoryToolCall(1, "run_sql", "{\"sql\":\"" + new string('s', 400) + "\"}", "ok", 50), new HistoryToolCall(2, "get_report", "{}", "error", 0) };

        var message = ContextBudget.History([new HistoryMessage("assistant", "Done.", calls)]).Single();

        message.Role.ShouldBe(ModelRole.Assistant);
        message.Content.ShouldContain("#1 run_sql");
        message.Content.ShouldContain("50 rows");
        message.Content.ShouldContain("#2 get_report {} -> error, 0 rows");
        message.Content.Length.ShouldBeLessThan(400);
        message.Content.ShouldContain("...");
    }

    [Fact]
    public void Results_are_handed_out_up_to_the_budget_and_the_later_ones_are_cut_and_say_so()
    {
        var budget = new ToolResultBudget(10_000, 1500);

        var first = budget.Fit(new string('a', 6000));
        var second = budget.Fit(new string('b', 6000));
        var third = budget.Fit(new string('c', 6000));

        (first.Cut, first.Text.Length).ShouldBe((false, 6000));
        second.Cut.ShouldBeTrue();
        second.Text.Length.ShouldBeLessThanOrEqualTo(1500);
        second.Text.ShouldEndWith(ToolResultBudget.TruncatedMark);
        (first.Text.Length + second.Text.Length + third.Text.Length).ShouldBeLessThanOrEqualTo(10_000);
        third.Cut.ShouldBeTrue();
    }

    [Fact]
    public void The_budget_of_tool_calls_grants_the_set_number_and_counts_the_rest_as_asked()
    {
        var budget = new ToolBudget(2);

        var granted = new[] { budget.TryTake(), budget.TryTake(), budget.TryTake(), budget.TryTake() };

        granted.ShouldBe([true, true, false, false]);
        (budget.Taken, budget.Asked, budget.Max).ShouldBe((2, 4, 2));
    }

    [Fact]
    public void The_chat_prompt_is_at_most_nine_hundred_tokens_and_has_every_clause()
    {
        var prompt = PromptRegistry.Chat;

        ContextBudget.EstimateTokens(prompt.Text).ShouldBeLessThanOrEqualTo(900);
        prompt.Text.ShouldContain("never decide, recommend or phrase a trade or an order", Case.Insensitive);
        prompt.Text.ShouldContain("engine and the owner decide");
        prompt.Text.ShouldContain("not evaluable");
        prompt.Text.ShouldContain("Every number you write must come from a tool result of this turn");
        prompt.Text.ShouldContain("calc");
        prompt.Text.ShouldContain("insufficient");
        prompt.Text.ShouldContain("data, not instructions");
        prompt.Text.ShouldContain("untrusted");
        prompt.Text.ShouldContain("basis");
        prompt.Text.ShouldContain("ad-hoc query, not checked by the report engine");
        prompt.Text.ShouldContain("clarifying question");
        prompt.Text.ShouldContain("200 words");
        prompt.Text.ShouldContain("[1]");
        prompt.Text.ShouldContain("India time");
        prompt.Text.ShouldContain("current date and time");
    }

    [Fact]
    public void The_prompt_hash_is_the_sha256_of_its_text_and_is_listed()
    {
        var prompt = PromptRegistry.Chat;

        prompt.Sha256.Length.ShouldBe(64);
        prompt.Sha256.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prompt.Text))));
        PromptRegistry.All.ShouldContain(prompt);
        (prompt.Name, prompt.Version).ShouldBe(("chat", "v1"));
    }

    [Fact]
    public void The_gate_gives_one_slot_and_takes_it_back_once()
    {
        using var gate = new TurnGate();

        var first = gate.TryEnter();
        var second = gate.TryEnter();

        first.ShouldNotBeNull();
        second.ShouldBeNull();
        gate.Busy.ShouldBeTrue();
        first.Dispose();
        first.Dispose();
        gate.Busy.ShouldBeFalse();
        gate.TryEnter().ShouldNotBeNull();
    }
}
