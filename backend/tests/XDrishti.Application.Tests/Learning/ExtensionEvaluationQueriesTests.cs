using System.Text.Json;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning.Extensions;
using static XDrishti.Application.Tests.Learning.EvaluationKit;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The read side of P15-10 (criterion 8): newest first, at most 100, the evidence block as JSON with unavailable as text, 404 and 400 as stable codes.</summary>
public sealed class ExtensionEvaluationQueriesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExtensionEvaluationRecord Record(int i, string family = "pead") =>
        new(i.ToString("D64", System.Globalization.CultureInfo.InvariantCulture), family, "P", null, "pead-1", "none", new string('a', 64), null, null, 4, 1.5, "quarter", 0, 0, 0, null, "{\"status\":\"unavailable\"}", "NOT_EVALUABLE", ["seasons 8 of 20"], ["l"], "{\"verdict\":\"NOT_EVALUABLE\"}", "{\"sources\":{}}", false);

    [Fact]
    public async Task The_list_is_newest_first_capped_at_one_hundred_and_carries_the_blocks_as_json()
    {
        var store = new Store([]);
        for (var i = 1; i <= 130; i++)
        {
            await store.AppendAsync(Record(i), DateTimeOffset.UnixEpoch, Ct);
        }

        await store.AppendAsync(Record(999, "filter:results"), DateTimeOffset.UnixEpoch, Ct);

        var list = (await new ListExtensionEvaluationsHandler(store).HandleAsync(new ListExtensionEvaluationsQuery("pead"), Ct)).Value;

        list.Items.Count.ShouldBe(100);
        list.Items[0].Id.ShouldBe(130.ToString("D64", System.Globalization.CultureInfo.InvariantCulture));
        list.Items[0].Estimates.GetProperty("status").GetString().ShouldBe("unavailable");
        list.Items[0].Evidence.GetProperty("verdict").ValueKind.ShouldBe(JsonValueKind.String);
        list.Items[0].Missing.ShouldBe(["seasons 8 of 20"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ext:md-1")]
    [InlineData("PEAD")]
    public async Task An_unknown_or_missing_family_is_invalid_query(string? family)
    {
        var result = await new ListExtensionEvaluationsHandler(new Store([])).HandleAsync(new ListExtensionEvaluationsQuery(family), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("extensions.invalid_query");
    }

    [Fact]
    public async Task One_record_by_id_and_an_unknown_or_malformed_id_is_not_found()
    {
        var store = new Store([]);
        await store.AppendAsync(Record(7), DateTimeOffset.UnixEpoch, Ct);
        var handler = new GetExtensionEvaluationHandler(store);

        (await handler.HandleAsync(new GetExtensionEvaluationQuery(7.ToString("D64", System.Globalization.CultureInfo.InvariantCulture)), Ct)).Value.Verdict.ShouldBe("NOT_EVALUABLE");
        (await handler.HandleAsync(new GetExtensionEvaluationQuery(8.ToString("D64", System.Globalization.CultureInfo.InvariantCulture)), Ct)).Error.Code.ShouldBe("extensions.not_found");
        (await handler.HandleAsync(new GetExtensionEvaluationQuery("x"), Ct)).Error.Code.ShouldBe("extensions.not_found");
    }
}
