namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>Every row id of the specs P3-03 to P3-03e: encoded (where) or named with the reason it is not.</summary>
internal static class IdTable
{
    internal sealed record Entry(string Spec, string Id, string[] Where, string? Reason);

    private static Entry Enc(string spec, string id, params string[] where) => new(spec, id, where, null);

    private static Entry No(string spec, string id, string reason) => new(spec, id, [], reason);

    private const string CostChain = "cost chain (ADR 0042): the costing reference is P3-08c and is not part of this brief; the walker part of the example is encoded as the row it names";

    internal static IReadOnlyList<Entry> All { get; } = Build();

    private static List<Entry> Build()
    {
        var l = new List<Entry>();

        // P3-03
        foreach (string f in new[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F9", "F12", "F14", "F15", "F16", "F17" })
        {
            l.Add(Enc("P3-03", f, "row:" + f));
        }

        l.Add(Enc("P3-03", "F13", "row:F13-buy", "row:F13-sell", "test:RuleRowTests.F13_both_sides_on_one_list_give_two_trades"));
        l.Add(Enc("P3-03", "F17b", "row:F17b-fill", "row:F17b-miss"));
        foreach (string w in new[] { "W1", "W2", "W3", "W5", "W6", "W7", "W8", "W10", "W13" })
        {
            l.Add(Enc("P3-03", w, "row:" + w));
        }

        l.Add(Enc("P3-03", "W4", "row:W4a", "row:W4b"));
        l.Add(Enc("P3-03", "W9", "row:W9-list-end", "row:W9-next-date"));
        l.Add(Enc("P3-03", "W9b", "row:W9b-5min", "row:W9b-6min"));
        l.Add(Enc("P3-03", "W11", "row:F6"));
        l.Add(No("P3-03", "W12", "failures that are not an outcome (unsorted epochs, a candidate not at its index, a non-finite stop, a reward-risk not above 0): ADR 0047 R14 says the oracle's inputs are well-formed and the engine's refusal is not an outcome of the walk"));
        l.Add(Enc("P3-03", "W14", "row:W14-stop", "row:W14-target"));

        // P3-03b
        for (int i = 1; i <= 9; i++)
        {
            l.Add(Enc("P3-03b", "B" + i, "row:B" + i));
        }

        for (int i = 1; i <= 25; i++)
        {
            l.Add(Enc("P3-03b", "H" + i, "row:H" + i));
        }

        foreach (string n in new[] { "N1", "N2", "N4", "N5" })
        {
            l.Add(Enc("P3-03b", n, "row:" + n));
        }

        l.Add(Enc("P3-03b", "N3", "row:N3-list-end", "row:N3-next-date"));

        // P3-03c (the cost chain)
        foreach (string c in new[] { "W1", "W1b", "W2", "W3", "W4", "W5", "W6", "W7", "W8", "L1" })
        {
            l.Add(No("P3-03c", c, CostChain));
        }

        // P3-03d
        for (int i = 1; i <= 7; i++)
        {
            l.Add(Enc("P3-03d", "K" + i, "test:CalendarTests.The_due_session_and_the_due_minute"));
        }

        foreach (string m in new[] { "M1", "M1b", "M2", "M3", "M3b", "M3c", "M4a", "M4b", "M5", "M6", "M7a", "M7b" })
        {
            l.Add(Enc("P3-03d", m, "row:" + m));
        }

        l.Add(Enc("P3-03d", "Overnight shift invariance (H rows)", "test:RuleRowTests.Overnight_shift_invariance_of_the_h_rows"));
        l.Add(Enc("P3-03d", "G1", "test:GateTests.G1_two_candidates_are_blocked_one_on_each_side_of_the_hold_end"));
        l.Add(Enc("P3-03d", "G2", "test:GateTests.G2_a_candidate_after_an_end_of_data_trade_is_blocked_even_after_its_exit_instant"));
        l.Add(Enc("P3-03d", "G2b", "test:GateTests.G2b_the_hold_end_of_a_max_hold_at_the_last_close_is_the_end_of_the_due_session"));
        l.Add(Enc("P3-03d", "G3", "test:GateTests.G3_a_blocked_candidate_is_position_open_and_not_limit_not_reached"));
        l.Add(No("P3-03d", "G4", "goes through the Simulator and the scanner (ORB over three days, 100 seeded lists): the scanner, the frame and the simulator have no second implementation in this task; the gate rule itself is G1 to G3 and the PositionOpen-is-swing-only rule is the walker without a Hold, which never rests or gates (RuleRowTests)"));
        l.Add(No("P3-03d", "G5", "the seam (the same candidates walked under two reward-risk values; a reflection test on EntryCandidate): a statement about the engine's types and the scanner, not about the walk"));
        l.Add(No("P3-03d", "G6", "needs the costing quantity skip (SkippedQuantity): cost chain, P3-08c"));
        l.Add(Enc("P3-03d", "Fixture R", "row:R"));
        foreach (string j in new[] { "J1", "J2", "J3", "J4" })
        {
            l.Add(No("P3-03d", j, "window cut of the replayer (TradesBeforeWindow, OpenAtEnd fates) through InstrumentReplayer and the scanner of fixture R: the walk itself over those bars is row R; the fates are the replay layer, which has no second implementation"));
        }

        l.Add(No("P3-03d", "Q1", "the flagged-day segment cut and ExcludedAcrossDiscontinuity are replay behaviour (segments, fates, DiscontinuityNote); the walker of the first segment ends EndOfData, which is row M5 (list ends at the segment's last session)"));
        l.Add(Enc("P3-03d", "Q2", "row:Q2"));

        // P3-03e
        foreach (string o in new[] { "O1", "O2", "O3", "O4", "O5", "O5b", "O7", "O8" })
        {
            l.Add(Enc("P3-03e", o, "row:" + o));
        }

        l.Add(Enc("P3-03e", "O6", "row:O6-0920", "row:O6-0921"));
        l.Add(Enc("P3-03e", "O9", "test:GateTests.O9_a_candidate_at_15_30_on_the_day_a_position_leaves_at_the_close_is_not_blocked"));
        l.Add(No("P3-03e", "DF1", "the daily scanner days (frame, 1D bar close at the 15:29 step, DecisionBarIndex counts): scanner and frame have no second implementation; the walker receives the resulting candidate as in O1"));
        l.Add(No("P3-03e", "DF2", "the stale daily bar (StaleBucketSteps, IncompleteBarsConsumed): scanner and frame, no second implementation"));
        return l;
    }
}
