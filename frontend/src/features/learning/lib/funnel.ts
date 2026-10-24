/** The lifecycle states of a cell, in the order of doc 09 section 7 (the server's lower-case codes). */
export const lifecycleStages = ['research', 'candidate', 'paper', 'active', 'paused', 'retired'] as const;
export type Stage = (typeof lifecycleStages)[number];

/** The most `cell-state` reads the funnel makes; cells with rows beyond it are counted as "state not read". */
export const maxStateReads = 30;

export interface FunnelCounts {
  stages: Record<Stage, number>;
  /** States the screen does not know by name, by their code (a server newer than the screen). */
  other: Record<string, number>;
  /** Cells that have changelog rows whose state was not read (over the cap, failed, or still loading). */
  notRead: number;
  /** Cells whose replayed rows do not add up (the server's `problems`). */
  inconsistent: number;
}

export interface FunnelInput {
  /** Every cell key of the snapshot (window all). */
  snapshotCells: readonly string[];
  /** The cell keys that have changelog rows, newest first, each once. */
  cellsWithRows: readonly string[];
  /** What `cell-state` answered, by cell key; a cell with rows and no entry here was not read. */
  read: ReadonlyMap<string, { state: string; problems: readonly string[] }>;
}

const isStage = (code: string): code is Stage => lifecycleStages.some((stage) => stage === code);

/** The cell keys whose state is read: the newest `maxStateReads` that have changelog rows. */
export const cellsToRead = (cellsWithRows: readonly string[]): string[] => cellsWithRows.slice(0, maxStateReads);

/**
 * Cells per lifecycle state. Every cell is in research until a changelog row says otherwise (ADR 0073), so the cells of the
 * snapshot without rows are research; a cell with rows is where the server's replay put it. A cell whose state was not read is
 * counted apart and never placed in a stage.
 */
export function countFunnel({ snapshotCells, cellsWithRows, read }: FunnelInput): FunnelCounts {
  const stages = Object.fromEntries(lifecycleStages.map((stage) => [stage, 0])) as Record<Stage, number>;
  const other: Record<string, number> = {};
  const withRows = new Set(cellsWithRows);
  stages.research += snapshotCells.filter((key) => !withRows.has(key)).length;
  let notRead = 0;
  let inconsistent = 0;
  for (const key of cellsWithRows) {
    const answer = read.get(key);
    if (!answer) {
      notRead += 1;
      continue;
    }
    if (isStage(answer.state)) stages[answer.state] += 1;
    else other[answer.state] = (other[answer.state] ?? 0) + 1;
    if (answer.problems.length > 0) inconsistent += 1;
  }
  return { stages, other, notRead, inconsistent };
}
