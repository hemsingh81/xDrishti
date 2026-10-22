import type { StrategyIssue } from '../api/labQueries';

export type MarkerSeverity = 'error' | 'warning' | 'info';

export interface EditorMarker {
  startLineNumber: number;
  startColumn: number;
  endLineNumber: number;
  endColumn: number;
  message: string;
  severity: MarkerSeverity;
}

const severityOf = (severity: string): MarkerSeverity =>
  severity === 'error' ? 'error' : severity === 'warning' ? 'warning' : 'info';

/** The problems that have a place in the file become editor markers; the others stay in the list only. */
export function toMarkers(problems: readonly StrategyIssue[]): EditorMarker[] {
  return problems.flatMap((p) => {
    if (p.line === null || p.column === null) return [];
    const endLine = p.endLine ?? p.line;
    const endColumn = p.endColumn ?? p.column + 1;
    return [
      {
        startLineNumber: p.line,
        startColumn: p.column,
        endLineNumber: endLine,
        endColumn: endLine === p.line && endColumn <= p.column ? p.column + 1 : endColumn,
        message: `${p.code}: ${p.message}`,
        severity: severityOf(p.severity),
      },
    ];
  });
}

/** "line 3, column 5" or nothing when the problem has no place in the file. */
export function placeOf(p: Pick<StrategyIssue, 'line' | 'column'>): string | undefined {
  return p.line === null ? undefined : `line ${p.line}${p.column === null ? '' : `, column ${p.column}`}`;
}
