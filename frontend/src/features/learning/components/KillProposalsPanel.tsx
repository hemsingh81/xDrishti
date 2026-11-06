import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, formatAsOf, formatCount, formatRatio } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { Facts } from '@/shared/ui/Facts';
import { killProposalsQuery, type KillProposal } from '../api/confidenceQueries';
import { autoMeaning, proposalStateLabel, ruleLabel } from '../lib/confidenceText';
import { figure, formatFine, formatFineSigned } from '../lib/figure';
import { Panel } from './Panel';

const decided = (row: KillProposal): string => {
  if (row.state === 'proposed') return 'Not decided';
  const who = row.actor ?? 'not stated';
  return `${who}${row.decidedAt ? `, ${formatAsOf(row.decidedAt)}` : ''}`;
};

/** The strategy's kill proposals (open first in the words, newest first in the order) and the automatic setting. Read only. */
export function KillProposalsPanel({ strategy }: { strategy: string }) {
  const query = useQuery(killProposalsQuery(strategy));
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
              { kind: 'text', id: 'state', header: 'State', render: (row) => proposalStateLabel(row.state) },
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
            ]}
          />
          {query.data.statements.map((sentence) => (
            <Typography key={sentence} variant="body2" color="text.secondary">
              {sentence}
            </Typography>
          ))}
          <Typography variant="body2" color="text.secondary">
            This tab only reads. Applying a proposal, switching the automatic setting and re-enabling a cell are the
            owner&apos;s acts and are done through the API for now; none of them places an order.
          </Typography>
        </Box>
      )}
    </Panel>
  );
}
