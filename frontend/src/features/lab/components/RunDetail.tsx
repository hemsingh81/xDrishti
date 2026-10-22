import { Alert, Box, Button, LinearProgress, List, ListItem, Stack, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { formatIstDateTime } from '@/shared/lib/format';
import { ErrorState, Facts, LoadingState } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import { cancelRun, isActive, runKeys, runQuery } from '../api/runQueries';
import { ReportPanel } from './ReportPanel';
import { VerdictPanel } from './VerdictPanel';

interface RunDetailProps {
  runId: string;
  poll: boolean;
}

/** One run: state and live progress, cancel, problems, the verdict with its gates and the report. */
export function RunDetail({ runId, poll }: RunDetailProps) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { data: run, error, isPending } = useQuery(runQuery(runId, poll));
  const cancel = useMutation({
    mutationFn: () => cancelRun(runId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: runKeys.all });
      toast.notify('Cancel requested; the run stops at the next safe point.', 'info');
    },
    onError: (e) => {
      toast.notify(e.message, 'error');
    },
  });

  if (isPending) return <LoadingState label="Loading the run" />;
  if (error) return <ErrorState error={error} />;

  const going = isActive(run.state);
  const { done, total } = run.progress;
  return (
    <Stack spacing={2}>
      <Facts
        items={[
          { label: 'Run', value: `${run.strategy.id} v${run.strategy.version} · ${run.method}` },
          { label: 'State', value: run.cancelRequested && going ? `${run.state} (cancel requested)` : run.state },
          { label: 'Stage', value: run.stage ?? 'none yet' },
          {
            label: 'Universe',
            value: `${run.universe.label}, ${run.universe.instruments.length} instruments${run.universe.sampled ? ` sampled from ${run.universe.requested}` : ''}`,
          },
          { label: 'Window', value: `${run.window.from} to ${run.window.to}` },
          { label: 'Started', value: run.startedAt ? formatIstDateTime(run.startedAt) : 'not yet' },
          ...(run.counts
            ? [
                {
                  label: 'Counts',
                  value: `${run.counts.trades} trades, ${run.counts.instrumentsRun} of ${run.counts.instruments} instruments replayed, ${run.counts.skipped} skipped, ${run.counts.openAtEnd} open at the end`,
                },
              ]
            : []),
        ]}
      />
      {going && (
        <Box>
          <LinearProgress
            aria-label="Run progress"
            variant={total > 0 ? 'determinate' : 'indeterminate'}
            value={total > 0 ? (done / total) * 100 : 0}
          />
          <Typography variant="caption" color="text.secondary" role="status">
            {total > 0 ? `${run.stage ?? run.state}: ${done} of ${total} instruments` : `${run.stage ?? run.state}…`}
          </Typography>
          <Box sx={{ mt: 1 }}>
            <Button
              onClick={() => {
                cancel.mutate();
              }}
              disabled={cancel.isPending || run.cancelRequested}
            >
              Cancel run
            </Button>
          </Box>
        </Box>
      )}
      {run.error && <Alert severity="error">{run.error}</Alert>}
      {run.problems.length > 0 && (
        <Alert severity="warning">
          <List dense disablePadding aria-label="Run problems">
            {run.problems.map((p) => (
              <ListItem key={`${p.stage}${p.path}${p.code}${p.message}`} disableGutters>
                {p.severity}: {p.message} ({p.code}, {p.stage}
                {p.path ? `, ${p.path}` : ''})
              </ListItem>
            ))}
          </List>
        </Alert>
      )}
      {run.labels.map((l) => (
        <Typography key={l} variant="caption" color="text.secondary">
          {l}
        </Typography>
      ))}
      {run.verdict && <VerdictPanel verdict={run.verdict} />}
      {run.state === 'succeeded' && run.method !== 'quick' && <ReportPanel runId={run.id} />}
    </Stack>
  );
}
