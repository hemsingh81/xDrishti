import { Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Suspense, useState } from 'react';
import { DataTable, formatAsOf } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { Facts } from '@/shared/ui/Facts';
import { rollbackQuery, type RollbackProposal } from '../api/mlQueries';
import { figure, formatFine } from '../lib/figure';
import { rollbackStateLabel, switchMeaning, triggerLabel } from '../lib/mlText';
import { ApplyRollbackDialog } from './ActDialogs';
import { Panel } from './Panel';

const decided = (row: RollbackProposal): string => {
  if (row.state === 'proposed') return 'Not decided';
  return `${row.actor ?? 'not stated'}${row.decidedAt ? `, ${formatAsOf(row.decidedAt)}` : ''}`;
};

/**
 * The rollback proposals of the champion and the automatic setting. Applying one is the owner's act (session and CSRF, no
 * password): it moves the champion to challenger and never re-promotes anything, so there is no champion until the owner names one.
 */
export function RollbackPanel() {
  const query = useQuery(rollbackQuery);
  const [applying, setApplying] = useState<number | null>(null);
  return (
    <Panel title="Rollback">
      {query.isPending && <LoadingState label="Loading the rollback proposals…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && (
        <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
          <Facts
            items={[
              {
                label: 'Automatic rollback',
                value: (
                  <>
                    <strong>{query.data.settings.autoRollback ? 'ON' : 'OFF'}</strong>.{' '}
                    {switchMeaning('autoRollback', query.data.settings.autoRollback)}
                  </>
                ),
              },
            ]}
          />
          <DataTable
            label="Rollback proposals, newest first"
            rows={query.data.items}
            rowKey={(row) => String(row.id)}
            status={
              query.data.items.length === 0
                ? { kind: 'empty', reason: 'No rollback has been proposed; no trigger has fired.' }
                : undefined
            }
            columns={[
              { kind: 'text', id: 'model', header: 'Model', render: (row) => `Model #${row.modelId}` },
              { kind: 'text', id: 'trigger', header: 'Trigger', render: (row) => triggerLabel(row.trigger) },
              {
                kind: 'metric',
                id: 'value',
                header: 'Figure judged',
                metric: (row) => figure(row.value, 'not stated'),
                format: formatFine,
              },
              {
                kind: 'metric',
                id: 'limit',
                header: 'Compared with',
                metric: (row) => figure(row.limit, 'not stated'),
                format: formatFine,
              },
              { kind: 'text', id: 'text', header: 'In words', render: (row) => row.text },
              { kind: 'text', id: 'cells', header: 'Cells in alarm', render: (row) => row.cells.join(', ') || 'none' },
              {
                kind: 'text',
                id: 'previous',
                header: 'Previous champion',
                render: (row) =>
                  row.previousChampionId === null
                    ? 'none'
                    : `Model #${row.previousChampionId}, ${row.previousQualifies ? 'qualified when proposed' : 'did not qualify when proposed'} (information only)`,
              },
              { kind: 'text', id: 'state', header: 'State', render: (row) => rollbackStateLabel(row.state) },
              { kind: 'text', id: 'proposed', header: 'Proposed', render: (row) => formatAsOf(row.proposedAt) },
              { kind: 'text', id: 'decided', header: 'Decided by', render: decided },
              {
                kind: 'text',
                id: 'act',
                header: 'Owner act',
                render: (row) =>
                  row.state === 'proposed' ? (
                    <Button
                      size="small"
                      variant="outlined"
                      onClick={() => {
                        setApplying(row.id);
                      }}
                      sx={{ minHeight: 44 }}
                    >
                      Apply rollback #{row.id}
                    </Button>
                  ) : (
                    'none'
                  ),
              },
            ]}
          />
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            Applying a rollback moves the champion to challenger (shadow). It never re-promotes the previous champion:
            there is no champion until you name the next one.
          </Typography>
          {query.data.statements.map((sentence) => (
            <Typography key={sentence} variant="body2" color="text.secondary">
              {sentence}
            </Typography>
          ))}
          <Typography variant="body2" color="text.secondary">
            Promoting a model is not done from this screen; it is the API or <code>xd learn</code>, and today every
            promotion is refused because the evidence it needs has no source yet.
          </Typography>
        </Box>
      )}
      {applying !== null && (
        <Suspense fallback={<LoadingState label="Loading the form…" />}>
          <ApplyRollbackDialog
            proposalId={applying}
            onClose={() => {
              setApplying(null);
            }}
          />
        </Suspense>
      )}
    </Panel>
  );
}
