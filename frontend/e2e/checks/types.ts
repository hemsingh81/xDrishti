export type Status = 'pass' | 'fail' | 'na';
export type State = 'loading' | 'error' | 'empty' | 'thin' | 'full' | 'unavailable';
export type Scheme = 'light' | 'dark';

export interface Result {
  check: string;
  status: Status;
  note: string;
}

/** The coordinates of one page load in the matrix. */
export interface Ctx {
  screen: string;
  width: number;
  scheme: Scheme;
  state: State;
}

export const pass = (check: string, note = ''): Result => ({ check, status: 'pass', note });
export const fail = (check: string, note: string): Result => ({ check, status: 'fail', note });
export const na = (check: string, note: string): Result => ({ check, status: 'na', note });

/** Helpers the page-side probes install (see dom.ts). */
export interface XdWindow extends Window {
  __sel: (element: Element) => string;
  __live: string[];
  __cls: number;
}
