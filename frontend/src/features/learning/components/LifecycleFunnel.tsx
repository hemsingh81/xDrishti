import { Box, Typography } from '@mui/material';
import { useQueries, useQuery } from '@tanstack/react-query';
import { useMemo } from 'react';
import { EvidenceBlock, formatCount, NonValue } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { cellsQuery, cellStateQuery, changelogLimit, changelogQuery } from '../api/learningQueries';
import { basisOf } from '../lib/cells';
import { cellsToRead, countFunnel, lifecycleStages, maxStateReads } from '../lib/funnel';
import { NoSnapshot } from './NoSnapshot';
import { Panel } from './Panel';

const stageLabel: Record<(typeof lifecycleStages)[number], string> = {
  research: 'Research',
  candidate: 'Candidate',
  paper: 'Paper',
  active: 'Active',
  paused: 'Paused',
  retired: 'Retired',
};

/**
 * Cells per lifecycle state. The count has no endpoint of its own, so it is built from the snapshot (every cell is in
 * research until a changelog row says otherwise) and the server's `cell-state` replay of each cell that has rows. A cell whose
 * state was not read is counted apart, in words, and never placed in a stage.
 */
export function LifecycleFunnel() {
  const cells = useQuery(cellsQuery('all'));
  const rows = useQuery(changelogQuery({ subjectKind: 'cell' }));

  const cellsWithRows = useMemo(
    () => [...new Set((rows.data ?? []).filter((row) => row.subjectKind === 'cell').map((row) => row.subject))],
    [rows.data],
  );
  const toRead = useMemo(() => cellsToRead(cellsWithRows), [cellsWithRows]);
  const states = useQueries({ queries: toRead.map((cell) => cellStateQuery(cell)) });

  const loading = cells.isPending || rows.isPending || states.some((state) => state.isPending);
  const read = new Map<string, { state: string; problems: readonly string[] }>();
  states.forEach((state, index) => {
    const key = toRead[index];
    if (key !== undefined && state.data) read.set(key, state.data);
  });
  const funnel = cells.data
    ? countFunnel({ snapshotCells: [...new Set(cells.data.map((cell) => cell.cellKey))], cellsWithRows, read })
    : undefined;
  const max = funnel ? Math.max(1, ...Object.values(funnel.stages), ...Object.values(funnel.other), funnel.notRead) : 1;

  return (
    <Panel title="Cell lifecycle">
      {cells.isError && <ErrorState error={cells.error} />}
      {rows.isError && <ErrorState error={rows.error} />}
      {!cells.isError && !rows.isError && loading && <LoadingState label="Reading cell states…" />}
      {cells.data?.length === 0 && !loading && <NoSnapshot />}
      {funnel && cells.data && cells.data.length > 0 && !loading && !rows.isError && (
        <>
          <Box
            component="ol"
            aria-label="Cells by lifecycle state"
            sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 1 }}
          >
            {lifecycleStages.map((stage) => (
              <Step key={stage} label={stageLabel[stage]} count={funnel.stages[stage]} max={max} />
            ))}
            {Object.entries(funnel.other).map(([code, count]) => (
              <Step key={code} label={`Other state: ${code}`} count={count} max={max} />
            ))}
            {funnel.notRead > 0 && <Step label="State not read" count={funnel.notRead} max={max} />}
          </Box>
          {funnel.notRead > 0 && (
            <Box sx={{ mt: 1 }}>
              <NonValue
                state={{
                  kind: 'unavailable',
                  reason: `The state of ${formatCount(funnel.notRead)} ${funnel.notRead === 1 ? 'cell' : 'cells'} with changelog rows was not read (at most ${formatCount(maxStateReads)} are read, or a read failed).`,
                }}
              />
            </Box>
          )}
          {funnel.inconsistent > 0 && (
            <Typography variant="body2" sx={{ mt: 1, color: 'warning.main', fontWeight: 600 }}>
              Warning: the changelog rows of {formatCount(funnel.inconsistent)} cell(s) do not add up; open the cell to
              see why.
            </Typography>
          )}
          <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1.5 }}>
            Every cell is in research until a changelog row says otherwise. A state is a record: nothing acts on it
            before paper data and live trading exist, and moves are made with the CLI, not on this screen.
          </Typography>
          <Box sx={{ mt: 1 }}>
            <EvidenceBlock
              label="Lifecycle evidence"
              n={cells.data.length}
              range={null}
              basis={basisOf(cells.data)}
              population={[
                `${formatCount(cells.data.length)} cell rows of the newest snapshot (window all)`,
                `changelog rows of cells, newest ${formatCount(changelogLimit)}`,
              ]}
              limits={
                rows.data.length >= changelogLimit
                  ? [
                      'The newest 200 changelog rows were read; there may be more, and older cells may be missing from the counts.',
                    ]
                  : []
              }
            />
          </Box>
        </>
      )}
    </Panel>
  );
}

function Step({ label, count, max }: { label: string; count: number; max: number }) {
  return (
    <Box
      component="li"
      sx={{ display: 'grid', gridTemplateColumns: 'minmax(96px, 140px) 1fr auto', alignItems: 'center', gap: 1 }}
    >
      <Typography component="span" variant="body2">
        {label}
      </Typography>
      {/* The bar repeats the number; the number is the information. */}
      <Box aria-hidden sx={{ height: 8, borderRadius: 1, backgroundColor: 'background.strong' }}>
        <Box
          sx={{
            height: '100%',
            width: `${String((count / max) * 100)}%`,
            borderRadius: 1,
            backgroundColor: 'primary.main',
          }}
        />
      </Box>
      <Typography component="span" variant="body2" sx={{ fontWeight: 700, minWidth: '2ch', textAlign: 'right' }}>
        {formatCount(count)}
      </Typography>
    </Box>
  );
}
