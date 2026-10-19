using System.Reflection;

namespace XDrishti.Simulation.Tests.Leaks;

/// <summary>
/// The inventory of the leak and invariance properties (spec P3-08b, Annex B (f); the table is also <c>docs/research/p3-08-leak-properties.md</c>): the property, the test that proves it, the test of its negative control, and whether it existed before P3-08b. The
/// test fails by reflection when a named test (a <c>[Fact]</c> or a <c>[Theory]</c> of this assembly) has been renamed or removed, and when the document does not name every test of the table, so the table cannot drift from the suite.
/// Two entries are outside this assembly and are listed by name, not checked: the <c>FeatureFrame.Reset()</c> equivalence (<c>XDrishti.Indicators.Tests</c>) and the cost chain's control (P3-08c).
/// </summary>
public sealed class PropertyInventoryTests(ITestOutputHelper output)
{
    private sealed record Row(string Property, bool Added, string[] Tests, string[] Controls);

    private static readonly Row[] Rows =
    [
        new("Look-ahead through the scanner's loop (truncation and perturbation)", false,
            ["LoopLookAheadPropertyTests.The_honest_loop_reports_no_violation_on_any_seed_for_any_strategy_and_side"],
            ["LoopLookAheadPropertyTests.The_peeking_feed_is_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none_on_the_same_inputs"]),
        new("Look-ahead through Simulator.Run, intraday (truncation and perturbation)", false,
            ["SimulatorLookAheadPropertyTests.The_honest_run_reports_no_violation_on_any_seed_for_any_strategy"],
            ["SimulatorLookAheadPropertyTests.The_peeking_feed_is_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none_on_the_same_inputs", "SimulatorLookAheadPropertyTests.The_peeking_walker_input_of_the_walkers_property_is_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none"]),
        new("Look-ahead at the walker (truncation and perturbation)", false,
            ["FutureAndOrderPropertyTests.An_outcome_settled_before_the_cut_does_not_change_when_the_minutes_after_it_are_removed_or_replaced"],
            ["FutureAndOrderPropertyTests.A_walker_that_peeks_one_minute_ahead_is_caught_on_at_least_ten_seeds_while_the_honest_walker_is_not"]),
        new("Fill timing: no fill in the signal minute", false,
            ["FillPropertyTests.Every_fill_and_exit_of_the_500_days_obeys_the_timing_range_grid_and_excursion_rules_with_the_doc_16_slippage"],
            ["FutureAndOrderPropertyTests.A_walker_that_decides_one_minute_early_breaks_the_fill_timing_check_of_criterion_19_on_at_least_ten_seeds_while_the_honest_one_never_does", "FillPropertyTests.The_check_is_not_vacuous_it_names_a_square_off_moved_a_minute_a_price_off_the_grid_and_a_fill_before_the_decision"]),
        new("Swing future independence, the gate included", false,
            ["SwingFuturePropertyTests.An_outcome_settled_at_or_before_a_cut_is_identical_when_the_minutes_after_it_are_removed_or_replaced_by_extreme_values"],
            ["SwingFuturePropertyTests.The_peek_next_session_control_is_caught_on_at_least_ten_series", "SwingFuturePropertyTests.The_calendar_from_the_future_control_is_caught_on_at_least_ten_series"]),
        new("The overnight order is settled by the minutes up to its fill minute (walker and gate)", false,
            ["DailySeededTests.The_entry_of_an_overnight_order_is_settled_by_the_minutes_up_to_the_fill_minute_and_by_nothing_after_it_over_500_series"],
            ["DailySeededTests.Control_a_limit_taken_from_the_high_of_the_fill_session_fails_on_many_series", "DailySeededTests.Control_a_walker_that_reads_the_pending_order_as_a_no_fill_bar_violates_the_property_on_many_series"]),
        new("Look-ahead through the replay (the prefix, flagged days, notes)", false,
            ["ReplayLookAheadPropertyTests.The_prefix_of_the_replay_is_the_same_when_the_series_ends_goes_on_or_is_replaced_by_extreme_bars_and_the_property_is_not_vacuous"],
            ["ReplayLookAheadPropertyTests.Control_extreme_bars_placed_in_the_first_session_show_a_difference_in_every_seed"]),
        new("Window independence of the swing replay (a later From, a later To)", false,
            ["SwingWindowPropertyTests.A_later_to_only_adds_sessions_trades_stay_open_trades_finish_and_a_blocked_candidate_stays_blocked"],
            ["SwingWindowPropertyTests.Control_a_replay_whose_scanner_starts_at_from_loses_the_history_and_fails_the_property_on_at_least_ten_series", "SwingWindowPropertyTests.Control_a_gate_that_starts_empty_at_from_walks_candidates_that_a_position_from_before_from_blocks_and_fails_on_at_least_ten_series"]),
        new("Determinism (the same input, the same bytes)", false,
            ["SimulatorDeterminismTests.The_seeded_series_render_the_same_text_twice", "WalkerDeterminismTests.Every_seeded_day_walked_twice_once_on_a_copy_of_the_minutes_renders_the_same_text_and_counters"],
            []),
        new("Slippage changes no decision, never improves a fill, is monotone", false,
            ["SlippagePropertyTests.The_trades_the_reasons_and_the_exact_prices_are_the_same_in_every_configuration", "SlippagePropertyTests.No_fill_is_ever_better_than_the_exact_price_so_filled_r_is_never_above_exact_r", "SlippagePropertyTests.Raising_one_parameter_never_raises_filled_r"],
            ["SlippagePropertyTests.Zero_slippage_is_the_exact_layer_and_default_slippage_strictly_costs_something_on_most_trades"]),
        new("The tick grid (rounding, the touch at a decimal level)", false,
            ["TickGridTests.A_grid_price_built_in_double_is_returned_unchanged_by_both_roundings", "TickGridTests.An_off_grid_price_rounds_down_to_the_grid_price_at_or_below_and_up_to_the_one_at_or_above"],
            ["TickGridTests.In_double_the_target_of_H21_and_H22_is_not_the_decimal_target"]),
        new("A night inserted into a swing walk (not a date shift)", false,
            ["SwingShiftInvarianceTests.The_same_trade_with_a_night_inserted_has_the_same_prices_ratios_excursions_and_counters_and_two_sessions"],
            ["SwingShiftInvarianceTests.A_gap_inserted_between_the_days_does_change_the_result_so_the_test_is_not_vacuous"]),
        new("RNet <= RFilled <= RExact", false,
            ["CostedPropertyTests.Net_R_is_not_above_filled_R_which_is_not_above_exact_R_and_is_below_it_whenever_something_was_charged"],
            []),
        new("Scale invariance (prices, volumes and tick by 2 and 4; first to cut)", true,
            ["ScaleInvarianceTests.Scaling_prices_volumes_and_the_tick_by_two_or_four_changes_no_decision_and_no_ratio_and_the_prices_scale_exactly", "ScaleInvarianceTests.The_built_in_strategies_have_no_rupee_threshold_only_periods_lags_and_multiples"],
            ["ScaleInvarianceTests.Control_a_fill_rounded_to_a_fixed_005_grid_after_scaling_is_reported_on_at_least_ten_seeds_while_the_honest_scaling_reports_none", "ScaleInvarianceTests.Control_a_threshold_in_rupees_is_reported_on_at_least_ten_seeds_so_the_comparison_can_see_a_scale_dependence"]),
        new("Date-shift invariance through Simulator.Run (whole weeks, calendar moved)", true,
            ["DateShiftInvarianceTests.Moving_the_series_and_its_calendar_by_whole_weeks_changes_nothing_but_the_dates"],
            ["DateShiftInvarianceTests.Control_a_calendar_left_at_the_old_dates_is_reported_on_at_least_ten_seeds_while_the_honest_shift_reports_none"]),
        new("Instrument-order independence (the replay, the assembly, the run key)", true,
            ["InstrumentOrderTests.A_universe_in_any_order_gives_the_same_result_for_every_instrument_the_same_trades_and_totals_and_a_key_that_follows_the_order"],
            ["InstrumentOrderTests.Control_a_fill_that_uses_a_last_price_shared_across_instruments_is_reported_on_at_least_ten_universes_while_the_honest_replay_reports_none"]),
        new("No state leaks between instruments (one prepared strategy over A, B, A)", true,
            ["StateLeakTests.One_prepared_strategy_run_over_A_then_B_then_A_in_another_order_gives_every_series_the_same_result_both_times"],
            ["StateLeakTests.Control_a_scanner_that_caches_the_previous_instruments_last_value_is_reported_on_at_least_ten_series_while_the_honest_run_reports_none"]),
        new("Daily truncation through Simulator.Run (the resting order, ADR 0043)", true,
            ["DailyTruncationTests.The_candidates_and_the_final_outcomes_of_a_prefix_are_those_of_the_full_run_over_all_500_daily_series_and_the_overnight_order_is_exercised"],
            ["DailyTruncationTests.Control_the_peeking_feeds_and_a_walker_that_reads_the_fill_session_are_reported_on_at_least_ten_seeds_while_the_honest_run_reports_none"]),
    ];

    /// <summary>Entries outside this assembly, named and not checked: the equivalence of a fresh frame and <c>Reset()</c> (the loop never calls <c>Reset</c>) and the cost chain's control, which is P3-08c.</summary>
    private static readonly string[] Elsewhere =
    [
        "XDrishti.Indicators.Tests: DeterminismAndResetTests, FrameColdStartTests (a fresh frame against Reset)",
        "P3-08c: the cost chain on the oracle and its flipped-cost-sign control",
    ];

    private static bool Exists(string name)
    {
        var parts = name.Split('.');
        var type = typeof(PropertyInventoryTests).Assembly.GetTypes().SingleOrDefault(t => t.Name == parts[0]);
        var method = type?.GetMethod(parts[1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        return method is not null && method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0;
    }

    [Fact]
    public void Every_test_the_inventory_names_exists_and_the_five_added_properties_are_in_the_leaks_namespace()
    {
        var names = Rows.SelectMany(r => r.Tests.Concat(r.Controls)).ToList();

        var missing = names.Where(n => !Exists(n)).ToList();

        output.WriteLine($"{Rows.Length} properties ({Rows.Count(r => r.Added)} added by P3-08b, {Rows.Count(r => !r.Added)} existing), {names.Count} named tests, {Elsewhere.Length} entries elsewhere");
        missing.ShouldBeEmpty();
        Rows.Count(r => r.Added).ShouldBe(5);
        Rows.Where(r => r.Added).SelectMany(r => r.Tests.Concat(r.Controls)).ShouldAllBe(n => typeof(PropertyInventoryTests).Assembly.GetTypes().Single(t => t.Name == n.Split('.')[0]).Namespace == typeof(PropertyInventoryTests).Namespace);
        Rows.Where(r => r.Controls.Length == 0).Select(r => r.Property).ShouldBe(["Determinism (the same input, the same bytes)", "RNet <= RFilled <= RExact"]);
    }

    [Fact]
    public void The_document_names_every_test_of_the_inventory_and_every_entry_elsewhere()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Oracle", "Docs", "p3-08-leak-properties.md");

        var text = File.ReadAllText(path);

        var absent = Rows.SelectMany(r => r.Tests.Concat(r.Controls)).Where(n => !text.Contains(n.Split('.')[1], StringComparison.Ordinal)).ToList();
        absent.ShouldBeEmpty();
        Elsewhere.ShouldAllBe(e => text.Contains(e.Split(':')[0], StringComparison.Ordinal));
        Rows.ShouldAllBe(r => text.Contains(r.Property, StringComparison.Ordinal));
    }
}
