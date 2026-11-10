import { Alert, Button, Chip, List, ListItem, ListItemText, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { formatIstDayAndTime, formatWeekdayDate } from '@/shared/lib/format';
import type { AutoState } from '../api/autoApi';
import { useAutoSwitch } from '../api/autoApi';
import { autoProblem, statusLabel } from '../lib/problem';
import { AutoOnDialog } from './AutoOnDialog';

/** The Auto switch, the chain, what stops it turning ON, and the last night (why it failed and where to recover). */
export function AutoSwitchCard({ auto }: { auto: AutoState }) {
  const [asking, setAsking] = useState(false);
  const off = useAutoSwitch();
  const { night } = auto;
  const failed = night.state === 'failed_closed';
  const lastChange = auto.changes[0];
  return (
    <Stack spacing={1.5}>
      <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography variant="h2" component="h2">
          Auto is {auto.on ? 'ON' : 'OFF'}
        </Typography>
        <Chip
          size="small"
          label={auto.on ? (auto.complete ? 'Chain complete' : 'Chain incomplete') : 'Nothing runs by itself'}
          color={auto.on && auto.complete ? 'success' : 'default'}
        />
        {auto.on ? (
          <Button
            variant="outlined"
            loading={off.isPending}
            onClick={() => {
              off.mutate({ on: false });
            }}
          >
            Turn Auto OFF
          </Button>
        ) : (
          <Button
            variant="contained"
            onClick={() => {
              setAsking(true);
            }}
          >
            Turn Auto ON…
          </Button>
        )}
      </Stack>
      <Typography color="text.secondary">{auto.noOrderNotice}</Typography>
      {off.isError && <Alert severity="error">{autoProblem(off.error)}</Alert>}
      {auto.note && <Alert severity="warning">{auto.note}</Alert>}
      {lastChange && (
        <Typography variant="body2" color="text.secondary">
          Last change: {lastChange.kind} by {lastChange.actor === 'auto' ? 'auto' : lastChange.actor},{' '}
          {formatIstDayAndTime(lastChange.at)} IST.
        </Typography>
      )}
      {failed || night.state === 'no_run' ? (
        <Alert
          severity={failed ? 'error' : 'info'}
          action={
            <Button component={RouterLink} to="/services/recovery" color="inherit" size="small">
              Recovery
            </Button>
          }
        >
          {failed
            ? `The last night failed closed${night.failedGate ? ` at ${night.failedGate}` : ''}: ${night.reason ?? 'no reason recorded'}. No suggestions are shown. `
            : 'No night has run yet. '}
          {night.forDate ? `The ${failed ? 'failed ' : ''}night was for ${formatWeekdayDate(night.forDate)}. ` : ''}Data
          last good: unavailable.
        </Alert>
      ) : (
        <Typography variant="body2" color="text.secondary">
          Last night: published{night.forDate ? ` for ${formatWeekdayDate(night.forDate)}` : ''}.
        </Typography>
      )}
      <Typography variant="h3" component="h3">
        What Auto needs
      </Typography>
      <List dense disablePadding aria-label="Preconditions">
        {auto.preconditions.map((p) => (
          <ListItem key={p.id} disableGutters>
            <ListItemText primary={`${p.label}: ${statusLabel(p.status)}`} secondary={p.detail} />
          </ListItem>
        ))}
      </List>
      <Typography variant="h3" component="h3">
        The chain ({auto.builder})
      </Typography>
      <List dense disablePadding aria-label="Chain">
        {auto.chain.map((s) => (
          <ListItem key={s.serviceId} disableGutters>
            <ListItemText
              primary={s.name}
              secondary={`${s.enabled ? 'On' : 'Paused'}${s.resumedByAuto ? ', resumed by Auto' : ''}`}
            />
          </ListItem>
        ))}
      </List>
      {asking && (
        <AutoOnDialog
          onClose={() => {
            setAsking(false);
          }}
        />
      )}
    </Stack>
  );
}
