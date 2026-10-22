import type { StrategyIssue } from '../api/labQueries';
import { placeOf, toMarkers } from './markers';

const issue = (over: Partial<StrategyIssue>): StrategyIssue => ({
  path: 'strategy.stop',
  code: 'strategy.bad',
  message: 'Bad.',
  severity: 'error',
  line: null,
  column: null,
  endLine: null,
  endColumn: null,
  start: null,
  length: null,
  suggestion: null,
  ...over,
});

describe('toMarkers', () => {
  it('marks problems that have a place and leaves the others to the list', () => {
    const markers = toMarkers([
      issue({ line: 3, column: 5, endLine: 3, endColumn: 9, severity: 'warning' }),
      issue({ path: 'strategy' }),
    ]);
    expect(markers).toEqual([
      {
        startLineNumber: 3,
        startColumn: 5,
        endLineNumber: 3,
        endColumn: 9,
        message: 'strategy.bad: Bad.',
        severity: 'warning',
      },
    ]);
  });

  it('gives a point problem one character of width and maps information to info', () => {
    const [m] = toMarkers([issue({ line: 2, column: 4, severity: 'information' })]);
    expect(m).toMatchObject({ endLineNumber: 2, endColumn: 5, severity: 'info' });
  });
});

describe('placeOf', () => {
  it('words the place', () => {
    expect(placeOf({ line: 3, column: 5 })).toBe('line 3, column 5');
    expect(placeOf({ line: null, column: null })).toBeUndefined();
  });
});
