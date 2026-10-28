import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import { Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { accountsQuery } from '@/features/accounts';
import { rampQuery, type RampView } from '../api/rampQueries';
import { stepNames } from '../lib/rampLabels';

/** One line per live account: step, live scale, hold, halt in force and the capture health of today. A value that is not known is a word, never a digit. */
function Line({ name, ramp }: { name: string; ramp: RampView }) {
  const today = ramp.loss?.date;
  const halted = ramp.halts.some((h) => h.kind === 'daily' && h.periodStart === today);
  const health = ramp.loss ? ramp.loss.health.replace('_', ' ') : 'not known';
  const scale =
    typeof ramp.scaleEffective === 'number'
      ? `${String(Math.round(ramp.scaleEffective * 100))} % of the risk`
      : 'no scale';
  return (
    <Typography>
      <strong>{name}:</strong>{' '}
      {ramp.step === null ? 'no step set' : (stepNames[ramp.step] ?? `step ${String(ramp.step)}`)}, {scale}.
      {ramp.hold ? ' On hold.' : ''}
      {halted ? ' A daily loss halt is in force: no new entries (advice, you place every order).' : ''} Capture {health}
      .
    </Typography>
  );
}

function AccountLine({ id, name }: { id: string; name: string }) {
  const q = useQuery(rampQuery(id));
  if (q.isPending) return <Typography color="text.secondary">{name}: loading…</Typography>;
  if (q.isError) return <Typography color="text.secondary">{name}: the ramp could not be read.</Typography>;
  return <Line name={name} ramp={q.data} />;
}

/** Today tile of the live phase (spec P11-04): the step, scale, halt and capture health of each manual-live account. It shows nothing when there is no such account. Loaded on demand. */
export function LiveTile() {
  const accounts = useQuery(accountsQuery);
  const live = (accounts.data ?? []).filter((a) => a.roles.trading.enabled && a.roles.trading.mode === 'manual_live');
  if (live.length === 0) return null;
  return (
    <Card component="section" aria-labelledby="live-tile">
      <CardContent>
        <Stack spacing={1}>
          <Typography id="live-tile" variant="h2">
            Live trading
          </Typography>
          {live.map((a) => (
            <AccountLine key={a.id} id={a.id} name={a.name} />
          ))}
          <div>
            <Button component={RouterLink} to="/live?tab=ramp" variant="contained" endIcon={<ArrowForwardIcon />}>
              Open the ramp
            </Button>
          </div>
        </Stack>
      </CardContent>
    </Card>
  );
}
