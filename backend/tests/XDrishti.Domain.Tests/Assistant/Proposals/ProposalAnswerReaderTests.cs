using XDrishti.Domain.Assistant.Proposals;

namespace XDrishti.Domain.Tests.Assistant.Proposals;

/// <summary>Spec P9-08 AC 2: the model's answer is data; a shape outside the schema does not get through.</summary>
public sealed class ProposalAnswerReaderTests
{
    private static readonly string Run = new('a', 64);

    private static string Hypothesis(string reason = "The cell has 120 trades.") =>
        "{\"kind\":\"hypothesis\",\"subject\":\"s\",\"payload\":{\"baselineRunKey\":\"" + Run + "\",\"cellKey\":\"ORB|15m|BUY|intraday|FIXED_RR|*\",\"knob\":{\"name\":\"rr\",\"kind\":\"Parameter\",\"baselineValue\":2,\"candidateValue\":\"2.5\"}},\"reason\":\"" + reason + "\",\"evidenceRefs\":[\"cell:ORB|15m|BUY|intraday|FIXED_RR|*\",\"run:" + Run + "\"]}";

    private static string Basket(string kind = "basket_add", string symbol = "evb") =>
        "{\"kind\":\"" + kind + "\",\"subject\":\"s\",\"payload\":{\"basketId\":\"" + Guid.Empty.ToString("D").Replace('0', '1') + "\",\"symbol\":\"" + symbol + "\"},\"reason\":\"r\",\"evidenceRefs\":[\"instrument:" + symbol + "\"]}";

    [Fact]
    public void A_good_answer_is_read_with_numbers_as_text_and_symbols_upper_case()
    {
        var read = ProposalAnswerReader.Read("{\"items\":[" + Hypothesis() + "," + Basket() + "]}");

        read.IsSuccess.ShouldBeTrue();
        read.Value.Refused.ShouldBeEmpty();
        var hypothesis = read.Value.Items[0].Payload.ShouldBeOfType<HypothesisPayload>();
        hypothesis.BaselineValue.ShouldBe("2");
        hypothesis.CandidateValue.ShouldBe("2.5");
        read.Value.Items[1].Payload.ShouldBeOfType<BasketPayload>().Symbol.ShouldBe("EVB");
    }

    [Fact]
    public void A_fenced_answer_and_an_empty_list_are_read()
    {
        ProposalAnswerReader.Read("```json\n{\"items\":[]}\n```").Value.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"items\":5}")]
    [InlineData("{\"items\":[],\"extra\":1}")]
    [InlineData("{}")]
    public void A_shape_outside_the_schema_is_refused(string text) => ProposalAnswerReader.Read(text).Error.Code.ShouldBe(ProposalCodes.AnswerShape);

    [Fact]
    public void More_than_three_items_is_refused()
    {
        var four = string.Join(',', Enumerable.Repeat(Basket(), 4));

        ProposalAnswerReader.Read("{\"items\":[" + four + "]}").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_bad_item_is_refused_alone_with_its_code()
    {
        var unknown = Basket().Replace("basket_add", "track_instrument", StringComparison.Ordinal);
        var longReason = Hypothesis(new string('x', 401));
        var extra = Basket().Replace("\"reason\"", "\"surprise\":1,\"reason\"", StringComparison.Ordinal);
        var noRefs = Basket().Replace("\"evidenceRefs\":[\"instrument:evb\"]", "\"evidenceRefs\":[]", StringComparison.Ordinal);
        var badSymbol = Basket(symbol: "a b");

        var read = ProposalAnswerReader.Read("{\"items\":[" + unknown + "," + longReason + "," + extra + "]}");
        var more = ProposalAnswerReader.Read("{\"items\":[" + noRefs + "," + badSymbol + "," + Basket() + "]}");

        read.Value.Items.ShouldBeEmpty();
        read.Value.Refused.ShouldBe([ProposalCodes.KindUnknown, ProposalCodes.ReasonInvalid, ProposalCodes.PayloadInvalid]);
        more.Value.Items.Count.ShouldBe(1);
        more.Value.Refused.ShouldBe([ProposalCodes.EvidenceUnresolved, ProposalCodes.PayloadInvalid]);
    }

    [Fact]
    public void Markup_and_control_characters_in_text_are_made_plain()
    {
        var read = ProposalAnswerReader.Read("{\"items\":[" + Hypothesis("<b>bold</b>\\u0007 text") + "]}");

        var reason = read.Value.Items.Single().Reason;
        reason.ShouldNotContain('<');
        reason.ShouldNotContain('\u0007');
    }

    [Fact]
    public void The_stored_payload_reads_back_and_a_damaged_one_does_not()
    {
        var payload = new HypothesisPayload(Run, "c", "rr", "Parameter", "2", "2.5");

        ProposalJson.ReadPayload(ProposalKind.Hypothesis, ProposalJson.Payload(payload)).ShouldBe(payload);
        var basket = new BasketPayload(Guid.NewGuid(), "EVB");
        ProposalJson.ReadPayload(ProposalKind.BasketRemove, ProposalJson.Payload(basket)).ShouldBe(basket);
        ProposalJson.ReadPayload(ProposalKind.Hypothesis, "{\"x\":1}").ShouldBeNull();
        ProposalJson.ReadPayload("bogus", "{}").ShouldBeNull();
    }
}
