/** Words for stored codes. A code not listed prints as sent. */
const lessonResults: Record<string, string> = { improved: 'Improved', no_measurable_change: 'No measurable change' };
const monitorKinds: Record<string, string> = {
  growing_tag: 'Growing loss tag',
  rolling_band: 'Rolling band',
  cusum: 'CUSUM drift',
  calibration: 'Calibration',
};

export const lessonResultWord = (result: string | null): string =>
  result === null ? 'Not closed' : (lessonResults[result] ?? result);
export const monitorKindWord = (kind: string): string => monitorKinds[kind] ?? kind;

export const lessonStates = ['all', 'open', 'linked', 'closed', 'dismissed'] as const;
export const hypothesisStates = [
  'all',
  'proposed',
  'approved',
  'running',
  'judged',
  'accepted',
  'rejected',
  'expired',
] as const;
export const modelRoles = ['all', 'challenger', 'champion', 'retired'] as const;

/** Where a move is made today, said beside every "may move to" line (D6: this screen has no control). */
export const moveNote = (verb: string): string =>
  `This screen cannot make the move; it is made with the CLI (xd learn ${verb}).`;

// shortcut: a pipe in the subject means a cell key; monitors name a model without one. A typed field would need an API change.
/** A cell key has pipes; a model subject does not. */
export const isCellKey = (subject: string): boolean => subject.includes('|');

/** Links within Learning: a fresh address (only these parameters), so no drawer of another tab lingers. */
export const learningLink = (params: Record<string, string | number>): string =>
  `?${new URLSearchParams(Object.entries(params).map(([k, v]) => [k, String(v)])).toString()}`;

/** A record id from the address; anything that is not a positive whole number is no id. */
export const parseId = (text: string | null): number | null => {
  const id = text !== null && /^[1-9][0-9]{0,14}$/.test(text) ? Number(text) : null;
  return id;
};
