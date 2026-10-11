import CloseIcon from '@mui/icons-material/Close';
import StorageIcon from '@mui/icons-material/StorageOutlined';
import { Alert, Button, Card, CardContent, IconButton, Stack, Typography } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import { useEffect, useId, useRef } from 'react';
import type { RefreshStatus } from '@/shared/api';
import { IconChip, LoadingState } from '@/shared/ui';
import { problemText } from '../lib/errors';
import { AutoRefreshLine } from './AutoRefreshLine';
import { RefreshProgress } from './RefreshProgress';
import { RefreshResult } from './RefreshResult';

interface RefreshPanelProps {
  /** Id for the panel, so the header's "last refreshed" line can point at it. */
  id: string;
  status: UseQueryResult<RefreshStatus>;
  /** A refresh was just started and the status does not show it yet. */
  busy: boolean;
  startError: unknown;
  onClose: () => void;
}

function Body({ data, busy, progressId }: { data: RefreshStatus; busy: boolean; progressId: string }) {
  if (data.current) {
    return <RefreshProgress run={data.current} id={progressId} />;
  }
  if (busy) {
    return (
      <Typography role="status" sx={{ fontWeight: 600 }}>
        Starting the refresh…
      </Typography>
    );
  }
  if (data.last) {
    return <RefreshResult run={data.last} />;
  }
  return <Typography sx={{ fontWeight: 600 }}>The master list has not been downloaded yet.</Typography>;
}

/**
 * The details of the master list: live progress while a refresh runs, then the result, any warnings, the counts and
 * the automatic schedule. It is only shown on request (the owner pressed Refresh, or opened it from the header) and is
 * dismissed with the close button.
 */
export function RefreshPanel({ id, status, busy, startError, onClose }: RefreshPanelProps) {
  const { data } = status;
  const titleId = useId();
  const progressId = useId();
  const panel = useRef<HTMLElement>(null);

  // It opens because the owner asked for it: take focus, so the keyboard and the screen reader land on it.
  useEffect(() => {
    panel.current?.focus();
  }, []);

  return (
    <Card
      id={id}
      ref={panel}
      tabIndex={-1}
      component="section"
      aria-labelledby={titleId}
      sx={{ mb: 2, flexShrink: 0, '&:focus': { outline: 'none' }, maxHeight: { md: '45%' }, overflow: { md: 'auto' } }}
    >
      <CardContent>
        <Stack direction="row" spacing={1.5} sx={{ alignItems: 'flex-start' }}>
          <IconChip icon={StorageIcon} hue="data" size="md" />
          <Stack spacing={1.25} sx={{ minWidth: 0, flex: 1 }}>
            <Typography id={titleId} variant="h2">
              Master list
            </Typography>
            {status.isPending && <LoadingState label="Loading refresh status" />}
            {status.isError && (
              <Alert
                severity="error"
                action={
                  <Button
                    color="inherit"
                    size="small"
                    onClick={() => {
                      void status.refetch();
                    }}
                  >
                    Retry
                  </Button>
                }
              >
                {problemText(status.error, 'Could not load the refresh status.')}
              </Alert>
            )}
            {data && <Body data={data} busy={busy} progressId={progressId} />}
            {startError ? (
              <Alert severity="error">{problemText(startError, 'The refresh could not be started.')}</Alert>
            ) : null}
            {data && <AutoRefreshLine auto={data.autoRefresh} />}
          </Stack>
          <IconButton aria-label="Close refresh details" edge="end" size="small" onClick={onClose}>
            <CloseIcon fontSize="small" />
          </IconButton>
        </Stack>
      </CardContent>
    </Card>
  );
}
