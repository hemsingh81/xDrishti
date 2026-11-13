import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { DataTable, MetricValue, formatCount, formatRate, formatRatio, type Column } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { shadowSummaryQuery, type ShadowGroup, type ShadowSummary } from '../api/shadowQueries';
import { toMetric } from '../lib/metric';
import { SequentialBlock } from './SequentialState';
import { EvidenceSetCard } from './EvidenceSetCard';
import { ShadowSwitchDialog } from './ShadowSwitchDialog';

const groupColumns: readonly Column<ShadowGroup>[] = [
  { kind: 'text', id: 'group', header: 'Group', render: (g) => g.group },
  { kind: 'text', id: 'n', header: 'Graded', align: 'right', render: (g) => formatCount(g.n) },
  {
    kind: 'text',
    id: 'mean',
    header: 'Mean R',
    align: 'right',
    render: (g) => <MetricValue state={toMetric(g.meanR)} />,
  },
  {
    kind: 'text',
    id: 'low',
    header: '5th pct',
    align: 'right',
    render: (g) => <MetricValue state={toMetric(g.low90)} />,
  },
  {
    kind: 'text',
    id: 'high',
    header: '95th pct',
    align: 'right',
    render: (g) => <MetricValue state={toMetric(g.high90)} />,
  },
  {
    kind: 'text',
    id: 'hit',
    header: 'Hit rate',
    align: 'right',
    render: (g) => <MetricValue state={toMetric(g.hitRate)} format={formatRate} />,
  },
];

const h2 = { fontSize: '1.1rem', mb: 1 } as const;

/** The Shadow tab: the forward record of every nightly candidate, graded by the paper replay. Read only; a separate count from P10. */
export function ShadowTab() {
  const summary = useQuery(shadowSummaryQuery);
  if (summary.isPending) return <LoadingState label="Reading the shadow record…" />;
  if (summary.isError) return <ErrorState error={summary.error} />;
  return <Body view={summary.data} />;
}

function Body({ view }: { view: ShadowSummary }) {
  const { counts, edge, chain, costs, sequential } = view;
  const [switching, setSwitching] = useState(false);
  return (
    <Stack spacing={3}>
      <Alert severity="info">
        {view.statement} {view.separateCount}
      </Alert>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography>Shadow record switch: {view.enabled ? 'ON' : 'OFF'}.</Typography>
        <Button
          variant="outlined"
          onClick={() => {
            setSwitching(true);
          }}
        >
          {view.enabled ? 'Switch off…' : 'Switch on…'}
        </Button>
      </Stack>
      {switching && (
        <ShadowSwitchDialog
          turnOn={!view.enabled}
          onClose={() => {
            setSwitching(false);
          }}
        />
      )}
      <EvidenceSetCard />
      {!view.hasRows && <Alert severity="info">{view.note ?? 'No shadow rows are stored yet.'}</Alert>}
      <Box component="section" aria-labelledby="shadow-sequential-heading">
        <Typography variant="h2" id="shadow-sequential-heading" sx={h2}>
          Sequential failure test
        </Typography>
        {sequential.available ? (
          <SequentialBlock
            edgeState={sequential.edgeState}
            behaviouralState={sequential.behaviouralState}
            line={sequential.line}
          />
        ) : (
          <Typography>Unavailable: {sequential.note ?? 'no reason given'}</Typography>
        )}
      </Box>
      <Box component="section" aria-labelledby="shadow-counts-heading">
        <Typography variant="h2" id="shadow-counts-heading" sx={h2}>
          Counts (never added)
        </Typography>
        <Box component="ul" aria-label="Counts" sx={{ m: 0, pl: 2.5 }}>
          <li>Behavioural (P10 exit criterion): {formatCount(counts.behavioural)}</li>
          <li>
            Edge (graded shadow trades): {formatCount(counts.edgeRaw)}, effective n {formatRatio(counts.edgeEffective)}
          </li>
          <li>
            Candidates: {formatCount(counts.candidates)}, counting for edge {formatCount(counts.edgeCandidates)}
          </li>
          <li>Outside the evidence set (written and graded, not counted): {formatCount(counts.outsideSet)}</li>
          <li>
            Pending {formatCount(counts.pending)}, not triggered {formatCount(counts.notTriggered)}, not gradable{' '}
            {formatCount(counts.notGradable)}
          </li>
        </Box>
      </Box>
      <Box component="section" aria-labelledby="shadow-edge-heading">
        <Typography variant="h2" id="shadow-edge-heading" sx={h2}>
          Effective n and pace
        </Typography>
        <Typography>
          {formatCount(edge.rawN)} graded over {formatCount(edge.sessions)} sessions; effective n{' '}
          {formatRatio(edge.effectiveN)} of {formatCount(edge.targetEffectiveN)}; same-day correlation{' '}
          {formatRatio(edge.rho)}
          {edge.rhoIsPlaceholder ? ' (placeholder)' : ''}.
        </Typography>
        {edge.rhoNote && <Typography color="text.secondary">{edge.rhoNote}</Typography>}
        <Typography color="text.secondary">{edge.etaNote}</Typography>
      </Box>
      <Box component="section" aria-labelledby="shadow-groups-heading">
        <Typography variant="h2" id="shadow-groups-heading" sx={h2}>
          By group
        </Typography>
        <DataTable label="Shadow groups" columns={groupColumns} rows={view.groups} rowKey={(g) => g.group} />
      </Box>
      <Box component="section" aria-labelledby="shadow-costs-heading">
        <Typography variant="h2" id="shadow-costs-heading" sx={h2}>
          Costs
        </Typography>
        <Typography>
          {costs.basis} ({costs.gross ? 'gross, before charges' : 'net of charges'}
          {costs.sheetIds.length > 0
            ? `; sheet ${costs.sheetIds.join(', ')}, ${costs.confirmed ? 'confirmed' : 'not confirmed'}`
            : ''}
          )
        </Typography>
      </Box>
      <Box component="section" aria-labelledby="shadow-chain-heading">
        <Typography variant="h2" id="shadow-chain-heading" sx={h2}>
          Chain
        </Typography>
        <Box component="ul" aria-label="Chain" sx={{ m: 0, pl: 2.5 }}>
          <li>Sessions: {formatCount(chain.sessions)}</li>
          <li>Gaps: {formatCount(chain.gaps)}</li>
          <li>Last session: {chain.lastSession ?? 'none yet'}</li>
          <li>Last digest: {chain.lastDigest ?? 'none yet'}</li>
        </Box>
      </Box>
    </Stack>
  );
}
