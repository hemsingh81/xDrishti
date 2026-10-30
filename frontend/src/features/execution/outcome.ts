export type Outcome = 'pass' | 'fail' | 'unavailable';

/** Anything the server did not say is `pass` or `fail` counts as unavailable: only `pass` is ever good. */
export const toOutcome = (value: string): Outcome => (value === 'pass' || value === 'fail' ? value : 'unavailable');
