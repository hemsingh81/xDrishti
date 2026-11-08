const three = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 3 });

/** A number is printed only when it is finite; anything else reads n/a. */
export const fin = (v: number | null | undefined): string =>
  typeof v === 'number' && Number.isFinite(v) ? three.format(v) : 'n/a';

type Known = 'pass' | 'fail' | 'unavailable';
/** An unknown status reads as unavailable, never as a pass. */
export const known = (status: string | null | undefined): Known =>
  status === 'pass' || status === 'fail' ? status : 'unavailable';
