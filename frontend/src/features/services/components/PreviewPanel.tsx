import { Box, Stack, Typography } from '@mui/material';
import type { SchedulePreview, ServiceIssue, ServiceView } from '@/shared/api';
import { formatIstDayAndTime } from '@/shared/lib/format';
import { IssueList } from './IssueList';

interface PreviewPanelProps {
  /** The schedule type of the draft. */
  type: string;
  /** The answer for the draft; undefined while there is none. */
  preview: SchedulePreview | undefined;
  /** The draft is not complete: the dialog says so instead of asking. */
  incomplete: boolean;
  checking: boolean;
  failed: boolean;
  /** The checks ran and found nothing at all (not even an error already written on a field). */
  allClear: boolean;
  /** The findings to list (the ones that are not already on a field). */
  issues: readonly ServiceIssue[];
  services: readonly ServiceView[];
  /** The name of the service the draft runs after (a run-after schedule); null otherwise. */
  followed: string | null;
}

/** The next three runs of the draft and what the checks found, updated as the owner types. */
export function PreviewPanel({
  type,
  preview,
  incomplete,
  checking,
  failed,
  allClear,
  issues,
  services,
  followed,
}: PreviewPanelProps) {
  const runs = preview?.nextRuns ?? [];
  return (
    <Box
      component="section"
      aria-label="Preview"
      aria-busy={checking}
      sx={{ p: 1.5, border: 1, borderColor: 'divider', borderRadius: 1, bgcolor: 'action.hover' }}
    >
      <Stack sx={{ gap: 1 }}>
        {incomplete ? (
          <Typography variant="body2" color="text.secondary">
            Complete the highlighted fields to see the next runs.
          </Typography>
        ) : (
          <div aria-live="polite">
            {preview && (
              <Typography variant="body2" sx={{ fontWeight: 600 }}>
                {preview.scheduleWords}
              </Typography>
            )}
            {runs.length > 0 ? (
              <Typography variant="body2">
                Next runs:{' '}
                {runs.map((at, index) => (
                  <span key={at}>
                    {index > 0 && ' · '}
                    <time dateTime={at}>{formatIstDayAndTime(at)}</time>
                  </span>
                ))}{' '}
                IST
              </Typography>
            ) : (
              preview && (
                <Typography variant="body2" color="text.secondary">
                  {followed
                    ? `Runs each time ${followed} finishes successfully.`
                    : type === 'continuous'
                      ? 'Always up.'
                      : 'No run ahead.'}
                </Typography>
              )
            )}
            {checking && (
              <Typography variant="caption" color="text.secondary">
                Checking…
              </Typography>
            )}
            {failed && (
              <Typography variant="caption" color="error.main">
                The checks could not be run. The server checks again when you save.
              </Typography>
            )}
          </div>
        )}
        <IssueList issues={issues} services={services} allClear={allClear && !checking} />
      </Stack>
    </Box>
  );
}
