import LockIcon from '@mui/icons-material/LockOutlined';
import { Alert, Box, LinearProgress, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatCount } from '@/shared/analytics';
import { formatDate, formatIstDateTime } from '@/shared/lib/format';
import { summaryQuery } from '../api/reportQueries';
import type { ReportQuery } from '../lib/reportFilter';
import { Panel } from './Panel';
import { ReportState } from './ReportState';

/**
 * How complete the data behind the report is (AU-14 completeness): chunks read of the job, the instant of the read, the stocks
 * with no trade, and the locked hold-out. Shares the summary request with the key figures, so it costs no extra call.
 */
export function TrainingBanner({ query }: { query: ReportQuery }) {
  const result = useQuery(summaryQuery(query));
  if (result.isError) return <ReportState error={result.error} />;
  const c = result.data?.evidence.completeness;
  if (!c) return null;
  const percent = c.chunksTotal === 0 ? 0 : Math.round((c.chunksDone / c.chunksTotal) * 100);
  return (
    <Panel title="How complete is this data">
      <Stack sx={{ gap: 1.5 }}>
        <Alert severity={c.complete ? 'success' : 'warning'}>
          {c.complete
            ? 'Complete: every chunk of the job is in this report.'
            : `Partial: ${formatCount(c.chunksDone)} of ${formatCount(c.chunksTotal)} chunks are in this report (${String(percent)} %). The job is ${c.jobState.replace(/_/g, ' ')}; the figures change as chunks finish.`}
        </Alert>
        <Box>
          <LinearProgress
            variant="determinate"
            value={percent}
            aria-label="Report data complete"
            sx={{ height: 8, borderRadius: 1 }}
          />
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            {`${formatCount(c.chunksDone)} done, ${formatCount(c.chunksRunning)} running, ${formatCount(c.chunksPending)} not started, ${formatCount(c.chunksFailed)} failed. Read as of ${formatIstDateTime(c.asOf)}.`}
          </Typography>
        </Box>
        <Typography variant="body2" color="text.secondary">
          {c.rangeFrom && c.rangeTo
            ? `Entries from ${formatDate(c.rangeFrom)} to ${formatDate(c.rangeTo)}; the training window is ${formatDate(c.windowFrom)} to ${formatDate(c.windowTo)}.`
            : `The range holds none of the training window (${formatDate(c.windowFrom)} to ${formatDate(c.windowTo)}).`}
        </Typography>
        <Alert severity="info" icon={<LockIcon fontSize="inherit" />}>
          {`Locked: the hold-out block from ${formatDate(c.holdOutStart)} is never read.${c.locked ? ` ${c.locked}` : ''}`}
        </Alert>
        {c.symbolsWithoutResults.length > 0 && (
          <Alert severity="info">{`No trade yet for: ${c.symbolsWithoutResults.join(', ')}.`}</Alert>
        )}
      </Stack>
    </Panel>
  );
}
