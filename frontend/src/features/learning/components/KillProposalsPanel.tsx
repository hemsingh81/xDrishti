import { Alert, Box, Button, Typography } from '@mui/material';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutlined';
import CancelIcon from '@mui/icons-material/CancelOutlined';
import HourglassEmptyIcon from '@mui/icons-material/HourglassEmptyOutlined';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { DataTable, formatAsOf, formatCount, formatRatio } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { Facts } from '@/shared/ui/Facts';
import { killProposalsQuery, type KillProposal } from '../api/confidenceQueries';
import { autoMeaning, proposalStateLabel, ruleLabel } from '../lib/confidenceText';
import { figure, formatFine, formatFineSigned } from '../lib/figure';
import type { KillTarget } from '../lib/killActText';
import { KillActDialog } from './KillActDialog';
import { PausedCells } from './PausedCells';
import { Panel } from './Panel';

const decided = (row: KillProposal): string => {
  if (row.state === 'proposed') return 'Not decided';
  const who = row.actor ?? 'not stated';
  const when = `${who}${row.decidedAt ? `, ${formatAsOf(row.decidedAt)}` : ''}`;
  return row.state === 'dismissed' ? `${when}. Reason: ${row.dismissReason ?? 'not stated'}` : when;
};

const stateIcon = (state: string) =>
  state === 'applied' ? (
    <CheckCircleOutlineIcon fontSize="inherit" aria-hidden />
  ) : state === 'dismissed' ? (
    <CancelIcon fontSize="inherit" aria-hidden />
  ) : (
    <HourglassEmptyIcon fontSize="inherit" aria-hidden />
  );

/** The strategy's kill proposals, the automatic setting and the owner's acts on them (CF-12; each one confirmed, none on load). */
export function KillProposalsPanel({ strategy }: { strategy: string }) {
  const query = useQuery(killProposalsQuery(strategy));
  // The act being confirmed (null = none): nothing is sent until the owner confirms it in the dialog.
  const [target, setTarget] = useState<KillTarget | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const ask = (t: KillTarget) => () => {
    setDone(null);
    setTarget(t);
  };
  // A cell may be paused under an applied proposal or, if it was dismissed during an apply, under a dismissed one: both are candidates,
  // and the cell's own lifecycle state decides whether a Re-enable button is shown.
  const candidateCells = [
    ...new Set(
      (query.data?.items ?? []).filter((p) => p.state === 'applied' || p.state === 'dismissed').flatMap((p) => p.cells),
    ),
  ];
  return (
    <Panel title="Kill proposals">
      {query.isPending && <LoadingState label="Loading the kill proposals…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && (
        <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
          <Facts
            items={[
              {
                label: 'Automatic setting',
                value: (
                  <>
                    <strong>{query.data.settings.auto ? 'ON' : 'OFF'}</strong>. {autoMeaning(query.data.settings.auto)}
                  </>
                ),
              },
              {
                label: 'Thresholds',
                value: `Cost multiplier ${formatRatio(query.data.settings.stressMultiplier)}; drawdown factor ${formatRatio(query.data.settings.drawdownFactor)}`,
              },
            ]}
          />
          <DataTable
            label="Kill proposals, newest first"
            rows={query.data.items}
            rowKey={(row) => String(row.id)}
            status={
              query.data.items.length === 0
                ? { kind: 'empty', reason: 'No kill proposal has been written for this strategy.' }
                : undefined
            }
            columns={[
              {
                kind: 'text',
                id: 'rule',
                header: 'Rule',
                render: (row) => (row.proposalOnly ? `${ruleLabel(row.rule)} (proposal only)` : ruleLabel(row.rule)),
              },
              {
                kind: 'text',
                id: 'state',
                header: 'State',
                render: (row) => (
                  <Box component="span" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5 }}>
                    {stateIcon(row.state)}
                    {proposalStateLabel(row.state)}
                  </Box>
                ),
              },
              {
                kind: 'metric',
                id: 'value',
                header: 'Figure judged',
                metric: (row) => figure(row.value, 'not stated'),
                format: formatFineSigned,
              },
              {
                kind: 'metric',
                id: 'limit',
                header: 'Limit',
                metric: (row) => figure(row.limit, 'not stated'),
                format: formatFine,
              },
              {
                kind: 'text',
                id: 'trades',
                header: 'Trades',
                render: (row) => formatCount(row.trades),
                align: 'right',
              },
              {
                kind: 'text',
                id: 'cells',
                header: 'Cells it would pause',
                render: (row) => row.cells.join(', ') || 'none',
              },
              { kind: 'text', id: 'proposed', header: 'Proposed', render: (row) => formatAsOf(row.proposedAt) },
              { kind: 'text', id: 'decided', header: 'Decided by', render: decided },
              { kind: 'text', id: 'text', header: 'In words', render: (row) => row.text },
              {
                kind: 'text',
                id: 'act',
                header: 'Owner act',
                render: (row) =>
                  row.state === 'proposed' ? (
                    <Box sx={{ display: 'flex', gap: 1 }}>
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={ask({ kind: 'apply', id: row.id, rule: row.rule, cells: row.cells })}
                      >
                        {`Apply proposal ${String(row.id)}`}
                      </Button>
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={ask({ kind: 'dismiss', id: row.id, rule: row.rule })}
                      >
                        {`Dismiss proposal ${String(row.id)}`}
                      </Button>
                    </Box>
                  ) : (
                    'none'
                  ),
              },
            ]}
          />
          {query.data.statements.map((sentence) => (
            <Typography key={sentence} variant="body2" color="text.secondary">
              {sentence}
            </Typography>
          ))}
          <PausedCells cells={candidateCells} ask={(key) => ask({ kind: 'reenable', key })} />
          <Typography variant="body2" color="text.secondary">
            Every act here is the owner&apos;s, explicit and confirmed; nothing runs when the page loads, and none of
            them places an order. Dismissing changes no cell.
          </Typography>
          {done !== null && (
            <Alert severity="success" role="status">
              {done}
            </Alert>
          )}
          <div>
            <Button variant="outlined" onClick={ask({ kind: 'auto', auto: !query.data.settings.auto })}>
              {query.data.settings.auto ? 'Turn automatic kill OFF' : 'Turn automatic kill ON'}
            </Button>
          </div>
        </Box>
      )}
      {target !== null && (
        <KillActDialog
          target={target}
          onDone={(message) => {
            setDone(message);
            setTarget(null);
          }}
          onClose={() => {
            setTarget(null);
          }}
        />
      )}
    </Panel>
  );
}
