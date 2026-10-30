import { Card, CardContent, CardHeader, List, ListItem, ListItemText } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { istDayIso } from '@/shared/lib/format';
import { feedStatusQuery, todayPlansQuery, type ReadinessAccount } from '../api/readinessApi';
import { toOutcome, type Outcome } from '../outcome';
import { CheckStatus } from './CheckStatus';

/** Four lines of the morning: Connect Dhan, Unlock today (not built yet), Plan approved, Feed connected. */
export function MorningChecklist({ account }: { account: ReadinessAccount }) {
  const plans = useQuery(todayPlansQuery(istDayIso()));
  const status = useQuery(feedStatusQuery);

  const connected = account.checks.find((c) => c.code === 'connected');
  const connect: Outcome = connected ? toOutcome(connected.outcome) : 'unavailable';
  const plan: Outcome = plans.data
    ? plans.data.some((p) => p.state === 'approved' || p.state === 'armed')
      ? 'pass'
      : 'fail'
    : 'unavailable';
  const feed: Outcome = status.data
    ? status.data.services.some((s) => s.name === 'xd-feed' && s.isAlive)
      ? 'pass'
      : 'fail'
    : 'unavailable';

  return (
    <Card component="section" aria-label="Morning checklist">
      <CardHeader title="Morning checklist" />
      <CardContent>
        <List dense component="ol">
          <Line n={1} label="Connect Dhan" outcome={connect} />
          <Line n={2} label="Unlock today" outcome="unavailable" note="Not yet available (P12-08)" />
          <Line n={3} label="Plan approved" outcome={plan} />
          <Line n={4} label="Feed connected" outcome={feed} />
        </List>
      </CardContent>
    </Card>
  );
}

function Line({ n, label, outcome, note }: { n: number; label: string; outcome: Outcome; note?: string }) {
  return (
    <ListItem secondaryAction={<CheckStatus outcome={outcome} />}>
      <ListItemText primary={`${n}. ${label}`} secondary={note} />
    </ListItem>
  );
}
