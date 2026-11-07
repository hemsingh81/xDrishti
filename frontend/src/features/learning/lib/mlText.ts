// The words the Models screen prints for the ML safety codes (CF-10). An unknown code is printed as the server sent it, never
// mapped to a pass.

const pick = (words: Readonly<Record<string, string>>, code: string | null | undefined): string =>
  code === null || code === undefined ? 'not stated' : (words[code] ?? code.replaceAll('_', ' '));

/** The four terms of the influence predicate. */
export const termLabel = (name: string): string =>
  pick(
    {
      HoldOutBeatsNoMl: 'Hold-out: beats the best no-ML baseline',
      ForwardShadowOk: 'Forward shadow candidates',
      NoCalibrationAlarm: 'No calibration alarm',
      SwitchOn: 'Owner switch ml.influence.enabled',
    },
    name,
  );

/** `holds` is a plain result; `fails` and `unavailable` both say they are not a pass. */
export const termStateLabel = (state: string): string =>
  pick({ holds: 'Holds', fails: 'Fails (not a pass)', unavailable: 'Unavailable (not a pass)' }, state);

export const holdOutStateLabel = (state: string): string =>
  pick(
    {
      not_kept: 'Not kept: no hold-out was set aside',
      kept: 'Kept: not looked at yet',
      used: 'Used: its one look is taken',
      reused: 'Reused: a second look is refused (not a pass)',
    },
    state,
  );

export type SwitchKey = 'influence' | 'autoPromote' | 'autoRollback';

export const switchKeys: readonly SwitchKey[] = ['influence', 'autoPromote', 'autoRollback'];

export const switchName = (key: SwitchKey): string =>
  ({ influence: 'ml.influence.enabled', autoPromote: 'ml.auto_promote', autoRollback: 'ml.auto_rollback' })[key];

export const switchTitle = (key: SwitchKey): string =>
  ({ influence: 'Model influence', autoPromote: 'Automatic promotion', autoRollback: 'Automatic rollback' })[key];

/** Turning influence or automatic promotion ON asks for a fresh password; every other change does not. */
export const needsPassword = (key: SwitchKey, on: boolean): boolean => on && key !== 'autoRollback';

/** What each switch means in each position. ON never opens anything alone and no switch places an order. */
export const switchMeaning = (key: SwitchKey, on: boolean): string => {
  switch (key) {
    case 'influence':
      return on
        ? 'ON: this is one of four terms. A model may influence a decision only when every term holds and it is the champion. Today the evidence terms have no source, so no model influences anything.'
        : 'OFF (default): no model may influence a decision. Models stay advisory.';
    case 'autoPromote':
      return on
        ? 'ON: the daily run may promote a challenger that meets the promotion rule and the predicate without this switch. It never promotes a model that has a rollback proposal.'
        : 'OFF (default, manual approval): nothing is promoted automatically; only the owner names a champion.';
    case 'autoRollback':
      return on
        ? 'ON: the daily run may apply a rollback proposal. The act is recorded as "auto" with the switch row, never as an owner approval. It lowers reliance on ML.'
        : 'OFF (default, manual approval): a trigger only writes a proposal. Nothing moves until the owner applies it.';
  }
};

export const triggerLabel = (trigger: string): string =>
  pick({ calibration: 'Calibration detector', band: 'Rolling band detector' }, trigger);

export const rollbackStateLabel = (state: string): string =>
  pick({ proposed: 'Open (proposed)', applied: 'Applied', dismissed: 'Dismissed' }, state);
