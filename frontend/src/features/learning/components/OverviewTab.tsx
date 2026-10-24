import { Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, formatAsOf, formatCount, MetricTile, type MetricState } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { overviewQuery, type LearningOverview, type OverviewSection } from '../api/learningQueries';
import { countState } from '../lib/overview';
import { NoSnapshot } from './NoSnapshot';
import { Panel } from './Panel';

const splitText = (section: OverviewSection): string | undefined =>
  section.split.length === 0
    ? undefined
    : section.split.map((part) => `${part.name} ${formatCount(part.count)}`).join(', ');

interface NewestRow {
  id: string;
  kind: string;
  label: string;
  verdict: string;
  detail: string;
  at: string;
}

function newestRows(overview: LearningOverview): NewestRow[] {
  const sections: [string, OverviewSection][] = [
    ['Models', overview.models],
    ['Experiments', overview.experiments],
    ['Hypotheses', overview.hypotheses],
    ['Lessons', overview.lessons],
    ['Changelog', overview.changelog],
  ];
  return sections.flatMap(([kind, section]) => [
    ...(section.newest ? [{ ...section.newest, id: `${kind}-newest`, kind: `${kind}, newest` }] : []),
    ...(section.champion ? [{ ...section.champion, id: `${kind}-champion`, kind: `${kind}, champion` }] : []),
  ]);
}

/** Overview: what the learning engine holds. Counts and the newest stored verdicts; nothing is computed or scored here. */
export function OverviewTab({ onOpenSetups }: { onOpenSetups: () => void }) {
  const query = useQuery(overviewQuery);
  if (query.isPending) return <LoadingState label="Loading the overview…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const o = query.data;
  const noSnapshot = o.cellRows === 0 || o.cellsAsOf === null;
  const snapshotState: MetricState = noSnapshot
    ? { kind: 'empty', reason: 'No snapshot yet; run `xd learn cells`.' }
    : { kind: 'value', value: o.cellRows };
  const tiles: { label: string; state: MetricState; hint?: string }[] = [
    {
      label: 'Cell rows',
      state: snapshotState,
      hint: o.cellsAsOf ? `Snapshot as of ${formatDate(o.cellsAsOf)}` : undefined,
    },
    { label: 'Models', state: countState(o.models, 'models'), hint: splitText(o.models) },
    { label: 'Experiments', state: countState(o.experiments, 'experiments'), hint: splitText(o.experiments) },
    { label: 'Hypotheses', state: countState(o.hypotheses, 'hypotheses'), hint: splitText(o.hypotheses) },
    { label: 'Lessons', state: countState(o.lessons, 'lessons'), hint: splitText(o.lessons) },
    { label: 'Changelog rows', state: countState(o.changelog, 'changelog rows'), hint: splitText(o.changelog) },
  ];
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 2 }}>
      {noSnapshot && (
        <Panel title="Cells">
          <NoSnapshot />
        </Panel>
      )}
      <Box
        sx={{
          display: 'grid',
          gap: 1.5,
          gridTemplateColumns: {
            xs: 'repeat(2, minmax(0, 1fr))',
            md: 'repeat(3, minmax(0, 1fr))',
            xl: 'repeat(6, minmax(0, 1fr))',
          },
          '--stat-tile-label-min': '2.5em',
        }}
      >
        {tiles.map((tile) => (
          <MetricTile key={tile.label} label={tile.label} state={tile.state} format={formatCount} hint={tile.hint} />
        ))}
      </Box>
      <Panel title="Newest stored record of each kind">
        <DataTable
          label="Newest records"
          rows={newestRows(o)}
          rowKey={(row) => row.id}
          columns={[
            { kind: 'text', id: 'kind', header: 'Kind', render: (row) => row.kind },
            { kind: 'text', id: 'label', header: 'Record', render: (row) => row.label },
            { kind: 'text', id: 'verdict', header: 'Stored verdict', render: (row) => row.verdict },
            { kind: 'text', id: 'detail', header: 'Detail', render: (row) => row.detail },
            { kind: 'text', id: 'at', header: 'Recorded', render: (row) => formatAsOf(row.at) },
          ]}
          evidence={{
            label: 'Overview evidence',
            n: null,
            range: null,
            basis: 'unknown',
            population: ['Stored learning records: the newest 200 of each kind are counted'],
            limits: o.limits,
          }}
        />
      </Panel>
      <Typography variant="body2" color="text.secondary">
        Each tile is a count of stored records; there is no combined health score. Lessons, experiments, hypotheses and
        models get their own tabs in later slices (P8-03 to P8-13).
      </Typography>
      <Box>
        <Button variant="outlined" onClick={onOpenSetups} sx={{ minHeight: 44 }}>
          Open Setups
        </Button>
      </Box>
    </Box>
  );
}
