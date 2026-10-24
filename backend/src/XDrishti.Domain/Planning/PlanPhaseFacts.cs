namespace XDrishti.Domain.Planning;

/// <summary>One stored attempt of the nightly pipeline, as the phase check reads it (spec P7-18).</summary>
/// <param name="Id">The row id.</param>
/// <param name="Attempt">1 for the first attempt of the planned session.</param>
/// <param name="Outcome"><c>published</c> or <c>failed_closed</c>.</param>
/// <param name="FailedGate">The gate or stage that blocked, or null.</param>
/// <param name="Reason">Why, in words.</param>
/// <param name="Manual">Whether a person started it.</param>
/// <param name="StartedAt">Start.</param>
/// <param name="FinishedAt">End.</param>
/// <param name="PlanId">The plan a published row names, or null.</param>
public sealed record PhaseRun(long Id, int Attempt, string Outcome, string? FailedGate, string Reason, bool Manual, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, long? PlanId);

/// <summary>The stored plan a published row names.</summary>
/// <param name="Id">The plan id.</param>
/// <param name="Basis">What the plan rests on; a skeleton plan starts with <c>skeleton</c>.</param>
/// <param name="Primary">Primary tickets.</param>
/// <param name="Reserve">Reserve tickets.</param>
/// <param name="Rejected">Rejected candidates.</param>
public sealed record PhasePlan(long Id, string Basis, int Primary, int Reserve, int Rejected);

/// <summary>Everything stored about one planned session: its attempts (oldest first) and the plan its published row names, if that plan is stored.</summary>
/// <param name="Date">The planned session.</param>
/// <param name="Runs">The attempts, oldest first.</param>
/// <param name="Plan">The plan, or null.</param>
public sealed record PhaseSessionInput(DateOnly Date, IReadOnlyList<PhaseRun> Runs, PhasePlan? Plan);

/// <summary>A gate (or stage) and how many failed-closed rows name it.</summary>
/// <param name="Gate">The id.</param>
/// <param name="Count">Rows.</param>
public sealed record PhaseGateCount(string Gate, int Count);

/// <summary>The failed-closed rows read (criterion 2).</summary>
/// <param name="RowsRead">How many run rows were read.</param>
/// <param name="Truncated">True when the read hit its limit, so older rows exist.</param>
/// <param name="ByGate">Failed-closed rows per gate, gate order.</param>
/// <param name="Inconsistent">Failed-closed rows without a gate or a reason, or that name a plan.</param>
public sealed record PhaseFailClosedFacts(int RowsRead, bool Truncated, IReadOnlyList<PhaseGateCount> ByGate, int Inconsistent);

/// <summary>The broker allow-list against its pinned expectation (criterion 4).</summary>
/// <param name="Current">The entries now in the code; empty when the list could not be read.</param>
/// <param name="Pinned">The entries expected.</param>
/// <param name="ProbesAllowed">Probe requests that name an order, position or fund endpoint and that the list nevertheless allows; must be empty.</param>
public sealed record PhaseAllowListFacts(IReadOnlyList<string> Current, IReadOnlyList<string> Pinned, IReadOnlyList<string> ProbesAllowed);

/// <summary>What the stored plans carry as data (criterion 6).</summary>
/// <param name="Plans">Plans read.</param>
/// <param name="Tickets">Primary and reserve tickets read.</param>
/// <param name="PlansWithoutNotice">Plans whose stored no-order sentence is not the fixed one.</param>
/// <param name="PlansWithoutLimits">Plans with no honest-limits sentence.</param>
/// <param name="TicketsWithoutEvidence">Tickets with no data range or no basis for the probability.</param>
public sealed record PhaseEvidenceFacts(int Plans, int Tickets, int PlansWithoutNotice, int PlansWithoutLimits, int TicketsWithoutEvidence);

/// <summary>The review workflow as stored (criterion 7).</summary>
/// <param name="Plans">Plans read.</param>
/// <param name="PlansLapsed">Plans in the lapsed state.</param>
/// <param name="PlansWithDecision">Plans with at least one stored approval row.</param>
/// <param name="OwnerApproved">Owner approvals.</param>
/// <param name="OwnerModified">Owner modifications.</param>
/// <param name="OwnerRejected">Owner rejections.</param>
/// <param name="AutoApproved">Approvals of kind auto.</param>
/// <param name="TicketsLapsed">Tickets in the lapsed state.</param>
/// <param name="UndecidedPastCutoff">Tickets still waiting for the cutoff job well after the cutoff.</param>
/// <param name="DecisionsAfterCutoff">Approval rows dated at or after their plan's cutoff.</param>
/// <param name="ApprovedWithoutRow">Tickets that are, or ever were, approved, armed or triggered (by state or by their trail) and have no approve row.</param>
/// <param name="AutoSwitchEverOn">Whether the auto-approval switch was ever stored as ON.</param>
/// <param name="AutoBeforeFirstOn">Auto approvals dated before the switch was first stored ON, or all of them when it never was.</param>
/// <param name="PlansCap">The most plans read (0 when not stated).</param>
/// <param name="PlansTruncated">Whether more plans exist than were read.</param>
/// <param name="AutoHistoryTruncated">Whether the autonomy history is longer than the rows read (the first ON row may be older).</param>
public sealed record PhaseReviewFacts(
    int Plans,
    int PlansLapsed,
    int PlansWithDecision,
    int OwnerApproved,
    int OwnerModified,
    int OwnerRejected,
    int AutoApproved,
    int TicketsLapsed,
    int UndecidedPastCutoff,
    int DecisionsAfterCutoff,
    int ApprovedWithoutRow,
    bool AutoSwitchEverOn,
    int AutoBeforeFirstOn = 0,
    int PlansCap = 0,
    bool PlansTruncated = false,
    bool AutoHistoryTruncated = false);

/// <summary>The per-account sizes stored on tickets (criterion 8).</summary>
/// <param name="Allocations">Allocation rows read.</param>
/// <param name="NotPaper">Allocations whose account mode is not paper.</param>
public sealed record PhaseAllocationFacts(int Allocations, int NotPaper);

/// <summary>The stored grades (criterion 9).</summary>
/// <param name="GradedPlans">Plans with at least one grade.</param>
/// <param name="PartlyGradedPlans">Graded plans with a ticket that has no grade yet (pending tickets are not written).</param>
/// <param name="GradedTickets">Tickets with a grade.</param>
/// <param name="TicketsOfGradedPlans">All tickets of the graded plans.</param>
public sealed record PhaseGradeFacts(int GradedPlans, int PartlyGradedPlans, int GradedTickets, int TicketsOfGradedPlans);

/// <summary>The stored journal (criterion 11).</summary>
/// <param name="Entries">Journal entries.</param>
/// <param name="Fills">Fill links.</param>
/// <param name="FillGrades">Entries with a fill grade.</param>
/// <param name="DuplicateFills">Fills linked to more than one entry.</param>
public sealed record PhaseJournalFacts(int Entries, int Fills, int FillGrades, int DuplicateFills);

/// <summary>A service of the framework and whether it runs.</summary>
/// <param name="Id">The service id.</param>
/// <param name="Role">What it is for in one phrase.</param>
/// <param name="Required">True when criterion 1 cannot be met without it.</param>
/// <param name="State"><c>enabled</c>, <c>paused</c> or <c>not registered</c>.</param>
public sealed record PhaseService(string Id, string Role, bool Required, string State);

/// <summary>One of the owner's switches.</summary>
/// <param name="Name">The setting.</param>
/// <param name="On">Whether it is ON.</param>
/// <param name="Effect">What it does, in a phrase, and whether the exit needs it.</param>
/// <param name="Needed">True when the pipeline cannot publish real tickets without it.</param>
public sealed record PhaseSwitch(string Name, bool On, string Effect, bool Needed);

/// <summary>A labelled count.</summary>
/// <param name="Name">What is counted.</param>
/// <param name="Value">The count.</param>
public sealed record PhaseCount(string Name, int Value);

/// <summary>The setup the pipeline runs under (services, switches, counts).</summary>
/// <param name="Services">The services.</param>
/// <param name="Switches">The switches.</param>
/// <param name="Counts">Counts of stored records (plans, grades, journal, digests, armed tickets).</param>
public sealed record PhaseSetupFacts(IReadOnlyList<PhaseService> Services, IReadOnlyList<PhaseSwitch> Switches, IReadOnlyList<PhaseCount> Counts);

/// <summary>All the stored facts the P7 phase check judges (spec P7-18). Plain data; the caller reads them, the pure check judges them.</summary>
/// <param name="Through">The last planned session of the window.</param>
/// <param name="Window">The planned sessions of the window, oldest first, one entry per trading session.</param>
/// <param name="FirstRecorded">The earliest planned session with any run row; null when there is none; <see cref="DateOnly.MinValue"/> when more rows exist than were read.</param>
/// <param name="FailClosed">Criterion 2.</param>
/// <param name="AllowList">Criterion 4.</param>
/// <param name="Evidence">Criterion 6.</param>
/// <param name="Review">Criterion 7.</param>
/// <param name="Allocation">Criterion 8.</param>
/// <param name="Grades">Criterion 9.</param>
/// <param name="Journal">Criterion 11.</param>
/// <param name="Setup">Services, switches and counts.</param>
public sealed record PlanPhaseFacts(
    DateOnly Through,
    IReadOnlyList<PhaseSessionInput> Window,
    DateOnly? FirstRecorded,
    PhaseFailClosedFacts FailClosed,
    PhaseAllowListFacts AllowList,
    PhaseEvidenceFacts Evidence,
    PhaseReviewFacts Review,
    PhaseAllocationFacts Allocation,
    PhaseGradeFacts Grades,
    PhaseJournalFacts Journal,
    PhaseSetupFacts Setup);
