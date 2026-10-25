/** The lifecycle states of a cell, in the order of doc 09 section 7 (the server's lower-case codes). */
export const lifecycleStages = ['research', 'candidate', 'paper', 'active', 'paused', 'retired'] as const;
export type Stage = (typeof lifecycleStages)[number];

/** The most `cell-state` reads the funnel makes; cells with rows beyond it are counted as "state not read". */
export const maxStateReads = 30;

export interface FunnelCounts {
  stages: Record<Stage, number>;
  /** States the screen does not know by name, by their code (a server newer than the screen). */
  other: Record<string, number>;
  /** Cells of the snapshot that have changelog rows whose state was not read (over the cap, failed, or still loading). */
  notRead: number;
  /** Cells whose replayed rows do not add up (the server's `problems`). */
  inconsistent: number;
  /** Cells the changelog names that are not in the newest snapshot: not counted in any stage. */
  outsideSnapshot: number;
  /**
   * True when the changelog read was cut at its limit: a cell that shows no row may have older rows, so `stages.research` is
   * then an upper bound (older rows can only move a cell out of research), never an exact count.
   */
  researchIsUpperBound: boolean;
}

export interface FunnelInput {
  /** Every cell key of the snapshot (window all). */
  snapshotCells: readonly string[];
  /** The cell keys that have changelog rows, newest first, each once (they may include cells outside the snapshot). */
  cellsWithRows: readonly string[];
  /** The changelog read returned its limit, so older rows exist that were not read. */
  truncated: boolean;
  /** What `cell-state` answered, by cell key; a cell with rows and no entry here was not read. */
  read: ReadonlyMap<string, { state: string; problems: readonly string[] }>;
}

const isStage = (code: string): code is Stage => lifecycleStages.some((stage) => stage === code);

/** The changelog's cells that are in the snapshot, newest first: the cells whose state is read and counted. */
export const rowCellsInSnapshot = (cellsWithRows: readonly string[], snapshotCells: readonly string[]): string[] => {
  const inSnapshot = new Set(snapshotCells);
  return cellsWithRows.filter((key) => inSnapshot.has(key));
};

/** The cell keys whose state is read: the newest `maxStateReads` of them. */
export const cellsToRead = (cells: readonly string[]): string[] => cells.slice(0, maxStateReads);

/**
 * Cells per lifecycle state, over the cells of the snapshot only (so the stages add up to the cells printed). Every cell is in
 * research until a changelog row says otherwise (ADR 0073), so a snapshot cell without rows is counted as research; with a
 * truncated changelog that count is an upper bound. A cell with rows is where the server's replay put it; one whose state was
 * not read is counted apart and never placed in a stage.
 */
export function countFunnel({ snapshotCells, cellsWithRows, truncated, read }: FunnelInput): FunnelCounts {
  const stages = Object.fromEntries(lifecycleStages.map((stage) => [stage, 0])) as Record<Stage, number>;
  const other: Record<string, number> = {};
  const inSnapshot = new Set(snapshotCells);
  const withRows = rowCellsInSnapshot(cellsWithRows, snapshotCells);
  const withRowsSet = new Set(withRows);
  stages.research += [...inSnapshot].filter((key) => !withRowsSet.has(key)).length;
  let notRead = 0;
  let inconsistent = 0;
  for (const key of withRows) {
    const answer = read.get(key);
    if (!answer) {
      notRead += 1;
      continue;
    }
    if (isStage(answer.state)) stages[answer.state] += 1;
    else other[answer.state] = (other[answer.state] ?? 0) + 1;
    if (answer.problems.length > 0) inconsistent += 1;
  }
  return {
    stages,
    other,
    notRead,
    inconsistent,
    outsideSnapshot: cellsWithRows.length - withRows.length,
    researchIsUpperBound: truncated,
  };
}
