import { Alert, Box, Button, Card, Chip, List, ListItem, ListItemText, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Suspense, useEffect, useState } from 'react';
import { PasswordReasonDialog } from '@/features/execution';
import { formatIstDayAndTime } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { recorderQuery, useChangeRecorder, type RecorderKind } from '../api/recordersApi';

const historyShown = 5;

interface Props {
  kind: RecorderKind;
  title: string;
}

/** One recorder: its switch, state, why it is not running, the notice and the last changes; the switch asks for the password and a reason. */
export function RecorderCard({ kind, title }: Props) {
  const query = useQuery(recorderQuery(kind));
  const change = useChangeRecorder(kind);
  // What the dialog will set, fixed when it opens (null = closed), so a refetch under it cannot flip the act.
  const [target, setTarget] = useState<boolean | null>(null);
  const [changed, setChanged] = useState(false);
  const headingId = `recorder-${kind}`;
  const { reset } = change;
  const close = () => {
    setTarget(null);
    reset();
  };
  useEffect(() => reset, [reset]);

  return (
    <Card component="section" aria-labelledby={headingId} sx={{ p: 2 }}>
      <Typography id={headingId} component="h3" variant="subtitle1" sx={{ fontWeight: 600 }}>
        {title}
      </Typography>
      {query.isPending && <LoadingState label={`Loading ${title}`} />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && (
        <>
          <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap', my: 1 }}>
            <Chip size="small" label={`Switch ${query.data.enabled ? 'ON' : 'OFF'}`} />
            <Chip size="small" variant="outlined" label={`State: ${query.data.state ?? 'not reported'}`} />
            <Button
              size="small"
              variant="outlined"
              aria-label={`${query.data.enabled ? 'Turn off' : 'Turn on'} ${title}`}
              onClick={() => {
                setChanged(false);
                setTarget(!query.data.enabled);
              }}
            >
              {query.data.enabled ? 'Turn off' : 'Turn on'}
            </Button>
          </Box>
          {query.data.notRunningBecause.length === 0 ? (
            <Typography variant="body2">It is running, or waiting for its trading window.</Typography>
          ) : (
            <>
              <Typography variant="body2" sx={{ fontWeight: 600 }}>
                Not running because
              </Typography>
              <List dense disablePadding aria-label={`${title}: not running because`}>
                {query.data.notRunningBecause.map((reason) => (
                  <ListItem key={reason} disableGutters>
                    <ListItemText primary={reason} />
                  </ListItem>
                ))}
              </List>
            </>
          )}
          <Typography variant="body2" color="text.secondary" sx={{ my: 1 }}>
            {query.data.notice}
          </Typography>
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            Recent changes
          </Typography>
          {query.data.history.length === 0 ? (
            <Typography variant="body2" color="text.secondary">
              The switch was never changed.
            </Typography>
          ) : (
            <List dense disablePadding aria-label={`${title}: recent changes`}>
              {query.data.history.slice(0, historyShown).map((h) => (
                <ListItem key={h.id} disableGutters>
                  <ListItemText
                    primary={`${h.enabled ? 'Turned on' : 'Turned off'} by ${h.actor}, ${formatIstDayAndTime(h.changedAt)} IST`}
                    secondary={h.reason}
                  />
                </ListItem>
              ))}
            </List>
          )}
          {changed && <Alert severity="success">The switch was changed.</Alert>}
        </>
      )}
      {target !== null && (
        <Suspense fallback={null}>
          <PasswordReasonDialog
            open
            title={`${target ? 'Turn on' : 'Turn off'} the ${title.toLowerCase()}`}
            confirmLabel={target ? 'Turn on' : 'Turn off'}
            pending={change.isPending}
            error={change.error}
            intro={
              <Typography variant="body2" sx={{ mb: 1 }}>
                Your password is asked again for this change.
              </Typography>
            }
            onSubmit={({ password, reason }) => {
              change.mutate(
                { enabled: target, reason, password },
                {
                  onSuccess: () => {
                    setChanged(true);
                    close();
                  },
                },
              );
            }}
            onClose={close}
          />
        </Suspense>
      )}
    </Card>
  );
}
