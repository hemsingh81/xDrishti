using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

public sealed class TradeFeatureCatalogTests
{
    [Fact]
    public void The_names_are_unique_and_the_stored_ones_come_before_the_cell_ones()
    {
        var names = TradeFeatureCatalog.Names;

        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(names.Count);
        names.Count.ShouldBe(TradeFeatureCatalog.StoredCount + TradeFeatureCatalog.CellCount);
        TradeFeatureCatalog.All.Take(TradeFeatureCatalog.StoredCount).ShouldAllBe(d => d.Source != TradeFeatureSource.Cell);
        TradeFeatureCatalog.All.Skip(TradeFeatureCatalog.StoredCount).ShouldAllBe(d => d.Source == TradeFeatureSource.Cell);
    }

    [Fact]
    public void The_catalog_of_version_tf_1_is_pinned_by_name_and_position()
    {
        TradeFeatureCatalog.Version.ShouldBe("tf-1");
        string.Join(',', TradeFeatureCatalog.Names).ShouldBe(
            "rsi14,atr_pct14,adx14,plus_di14,minus_di14,ema20_dist_pct,ema50_dist_pct,macd_hist_pct,bb_width20,bb_pos20,roc10,rel_volume20,vwap_dist_pct,range_pos20,supertrend_dir,gap_pct,prev_close_dist_pct,day_range_pos,prev_day_range_pct,day_open_dist_pct,"
            + "bar_range_pct,bar_body_pct,day_of_week,is_expiry_day,index_trend,index_adx,index_vol_pct,vix,vix_pct,vix_chg,breadth,regime,minute_of_day,side_sign,risk_pct,planned_rr,cell_n,cell_win_posterior,cell_win_lower,cell_expectancy_r,cell_enough");
    }

    [Fact]
    public void No_feature_name_is_an_outcome_so_nothing_after_the_decision_can_be_a_column()
    {
        string[] outcomes = ["exit", "result", "r_exact", "r_filled", "r_net", "mae", "mfe", "label", "sessions_held", "exit_reason", "pnl"];

        TradeFeatureCatalog.Names.ShouldAllBe(n => !outcomes.Any(o => n.Contains(o, StringComparison.Ordinal)));
    }

    [Fact]
    public void The_default_required_set_leaves_out_the_context_features_and_the_cell_expectancy()
    {
        TradeFeatureCatalog.DefaultRequired.ShouldNotContain("vix");
        TradeFeatureCatalog.DefaultRequired.ShouldNotContain("regime");
        TradeFeatureCatalog.DefaultRequired.ShouldNotContain("cell_expectancy_r");
        TradeFeatureCatalog.DefaultRequired.ShouldContain("rsi14");
        TradeFeatureCatalog.DefaultRequired.ShouldContain("cell_win_posterior");
        TradeFeatureCatalog.IndexOf("nonsense").ShouldBe(-1);
    }
}
