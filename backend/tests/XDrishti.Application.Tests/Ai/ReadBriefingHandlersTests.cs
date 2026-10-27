using XDrishti.Application.Ai.Briefing;

namespace XDrishti.Application.Tests.Ai;

/// <summary>Spec P9-06 AC 12: the reads and the preview write nothing and never call the model.</summary>
public sealed class ReadBriefingHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_preview_shows_the_fixed_format_text_for_any_plan_date_and_stores_nothing()
    {
        var rig = new BriefingRig();
        var handler = new PreviewBriefingHandler(rig.Source, rig.SettingsStore(), rig.Options, rig.Time);

        var result = await handler.HandleAsync(new PreviewBriefingQuery("daily", "2026-09-01"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBeNull();
        result.Value.Source.ShouldBe("deterministic");
        result.Value.Text.ShouldContain("AAA BUY");
        result.Value.Evidence.PlanId.ShouldBe(42);
        rig.Store.Inserts.ShouldBe(0);
        rig.Writer.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_preview_of_a_bad_kind_or_date_is_invalid()
    {
        var rig = new BriefingRig();
        var handler = new PreviewBriefingHandler(rig.Source, rig.SettingsStore(), rig.Options, rig.Time);

        (await handler.HandleAsync(new PreviewBriefingQuery("hourly", null), Ct)).Error.Code.ShouldBe(BriefingErrors.InvalidCode);
        (await handler.HandleAsync(new PreviewBriefingQuery("daily", "28-10-2026"), Ct)).Error.Code.ShouldBe(BriefingErrors.InvalidCode);
        (await handler.HandleAsync(new PreviewBriefingQuery("weekly", "2026-W99"), Ct)).Error.Code.ShouldBe(BriefingErrors.InvalidCode);
    }

    [Fact]
    public async Task A_stored_text_is_read_back_and_a_missing_one_is_not_found()
    {
        var rig = new BriefingRig();
        rig.Options.UseModel = false;
        await rig.Handler().HandleAsync(BriefingRig.Daily(), Ct);
        var get = new GetBriefingHandler(rig.Store, rig.Source, rig.Time);

        var found = await get.HandleAsync(new GetBriefingQuery("daily", "2026-10-28"), Ct);
        var newest = await get.HandleAsync(new GetBriefingQuery("daily", null), Ct);
        var missing = await get.HandleAsync(new GetBriefingQuery("daily", "2026-10-27"), Ct);

        found.Value.Subject.ShouldBe("2026-10-28");
        found.Value.Evidence.PlanId.ShouldBe(42);
        newest.Value.Id.ShouldBe(found.Value.Id);
        missing.Error.Code.ShouldBe(BriefingErrors.NotFoundCode);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public async Task The_list_limit_is_one_to_sixty(int limit, bool valid)
    {
        var rig = new BriefingRig();

        var result = await new ListBriefingsHandler(rig.Store).HandleAsync(new ListBriefingsQuery(null, limit), Ct);

        result.IsSuccess.ShouldBe(valid);
    }
}
