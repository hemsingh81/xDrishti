// The words the Decay and Loop tabs print for the server's codes (CF-12). An unknown code is printed as the server sent it,
// never mapped to a pass.

const pick = (words: Readonly<Record<string, string>>, code: string | null | undefined): string =>
  code === null || code === undefined ? 'not stated' : (words[code] ?? code.replaceAll('_', ' '));

export const ruleLabel = (rule: string): string =>
  pick(
    {
      cost_stress: 'Cost stress',
      drawdown: 'Drawdown',
      decay_twice: 'Decay twice in a row',
      failure_boundary: 'Failure boundary (CF-09)',
    },
    rule,
  );

/** The four kill-rule states. `held` and `not_evaluated` are never a pass. */
export const killStateLabel = (state: string): string =>
  pick(
    {
      fires: 'Fires',
      holds: 'Holds',
      held: 'Held (not a pass)',
      not_evaluated: 'Not evaluated (not a pass)',
    },
    state,
  );

export const seriesStateLabel = (state: string): string =>
  pick(
    {
      no_series: 'No series yet',
      unavailable: 'Not judged: no reference',
      monitoring: 'Monitoring: no completed window yet',
      ok: 'No alert (a flag, not a pass)',
      alert: 'Alert: the latest window is below its level',
    },
    state,
  );

export const proposalStateLabel = (state: string): string =>
  pick({ proposed: 'Open (proposed)', applied: 'Applied', dismissed: 'Dismissed' }, state);

export const armVerdictLabel = (verdict: string): string =>
  pick(
    {
      helps: 'Helps',
      hurts: 'Hurts',
      no_measurable_difference: 'No measurable difference',
      inconclusive: 'Inconclusive (fewer days than the minimum; not a pass)',
      unavailable: 'Not available',
    },
    verdict,
  );

export const armLabel = (name: string): string =>
  pick(
    {
      retrained_vs_frozen: 'Retrained against frozen (is retraining helping?)',
      retrained_vs_no_ml: 'Retrained against no model (is the model helping?)',
    },
    name,
  );

export const curveVerdictLabel = (verdict: string | null): string =>
  pick(
    {
      rising: 'Rising',
      flat: 'Flat',
      unclear: 'Unclear (a projection, not a finding)',
      insufficient: 'Insufficient points',
    },
    verdict,
  );

export const slopeVerdictLabel = (verdict: string | null): string =>
  pick(
    {
      ok: 'Inside the band',
      alarm: 'Alarm: outside the band',
      shown_only: 'Shown only: fewer outcomes than the window',
      unavailable: 'Not available',
    },
    verdict,
  );

/** Who put the automatic kill setting in force: the ADR 0282 seed row (a default, never an owner act) or the owner. */
export const autoSource = (actor: string | null | undefined): string =>
  actor === 'system:default' ? ' (system default, ADR 0282)' : actor ? ' (owner)' : '';

/** What the automatic kill setting means in each position. */
export const autoMeaning = (on: boolean): string =>
  on
    ? 'ON: when a kill rule fires, the daily monitor pauses the strategy\'s cells and records the decision as "auto", never as an owner approval. The CF-09 failure boundary is never executed automatically.'
    : 'OFF (manual approval): a firing rule only writes a proposal. Nothing is paused until the owner applies it.';
