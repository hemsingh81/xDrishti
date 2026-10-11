import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlineOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import SyncDisabledIcon from '@mui/icons-material/SyncDisabledOutlined';
import { Alert, Box, Chip, Stack, Typography } from '@mui/material';
import type { ReactElement } from 'react';
import type { RefreshRun } from '@/shared/api';
import { formatAgo, formatDateTime } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { summaryLine, triggerLabel } from '../lib/labels';
import { RefreshDetails } from './RefreshDetails';

const looks: Record<string, { label: string; color: 'success' | 'default' | 'error'; icon: ReactElement }> = {
  succeeded: { label: 'Updated', color: 'success', icon: <CheckCircleIcon /> },
  unchanged: { label: 'Unchanged', color: 'default', icon: <SyncDisabledIcon /> },
  failed: { label: 'Failed', color: 'error', icon: <ErrorIcon /> },
};

/** The last finished refresh: when, what changed, and any warning or error in plain words. */
export function RefreshResult({ run }: { run: RefreshRun }) {
  const look = looks[run.status] ?? { label: run.status, color: 'default' as const, icon: <SyncDisabledIcon /> };
  const finished = run.finishedAt ?? run.startedAt;

  return (
    <Stack spacing={1}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.5 }}>
        <Chip size="small" variant="outlined" color={look.color} icon={look.icon} label={look.label} />
        <Typography component="span" variant="body2" color="text.secondary" sx={numeric}>
          {triggerLabel(run.trigger)} · last refreshed <time dateTime={finished}>{formatAgo(finished)}</time> (
          {formatDateTime(finished)})
        </Typography>
      </Stack>
      {run.status === 'failed' ? (
        <Alert severity="error">
          The refresh failed{run.error ? `: ${run.error}` : '.'} The previous list is unchanged.
        </Alert>
      ) : (
        <Typography sx={{ fontWeight: 600, ...numeric }}>
          {run.status === 'unchanged' ? 'No changes — the file is unchanged' : summaryLine(run)}
        </Typography>
      )}
      {run.warnings.length > 0 && (
        <Alert severity="warning">
          <Typography component="p" sx={{ fontWeight: 600 }}>
            {run.warnings.length === 1 ? '1 warning' : `${String(run.warnings.length)} warnings`}
          </Typography>
          <Box component="ul" sx={{ m: 0, ps: 2.5 }}>
            {run.warnings.map((warning, index) => (
              <li key={`${String(index)}-${warning}`}>{warning}</li>
            ))}
          </Box>
        </Alert>
      )}
      {run.status !== 'failed' && <RefreshDetails run={run} />}
    </Stack>
  );
}
