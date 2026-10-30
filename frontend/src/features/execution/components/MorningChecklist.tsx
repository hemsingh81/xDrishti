import { Button, Card, CardContent, CardHeader, List, ListItem, ListItemText, Stack } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { istDayIso } from '@/shared/lib/format';
import { dayUnlocksQuery } from '../api/confirmApi';
import { feedStatusQuery, todayPlansQuery, type ReadinessAccount } from '../api/readinessApi';
import { toOutcome, type Outcome } from '../outcome';
import { CheckStatus } from './CheckStatus';
import { DayUnlockDialog } from './DayUnlockDialog';

/** Four lines of the morning: Connect Dhan, Unlock today, Plan approved, Feed connected. */
export function MorningChecklist({ account }: { account: ReadinessAccount }) {
  const plans = useQuery(todayPlansQuery(istDayIso()));
  const status = useQuery(feedStatusQuery);
  const unlocks = useQuery(dayUnlocksQuery);
  const [dialog, setDialog] = useState(false);
  const [now] = useState(() => Date.now());
  const unlocked: Outcome = unlocks.data
    ? unlocks.data.some((u) => u.accountId === account.accountId && Date.parse(u.expiresAt) > now)
      ? 'pass'
      : 'fail'
    : 'unavailable';

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
          <Line
            n={2}
            label="Unlock today"
            outcome={unlocked}
            action={
              unlocked !== 'pass' && (
                <Button
                  size="small"
                  onClick={() => {
                    setDialog(true);
                  }}
                >
                  Unlock today
                </Button>
              )
            }
          />
          <Line n={3} label="Plan approved" outcome={plan} />
          <Line n={4} label="Feed connected" outcome={feed} />
        </List>
      </CardContent>
      <DayUnlockDialog
        accountId={account.accountId}
        accountName={account.name}
        open={dialog}
        onClose={() => {
          setDialog(false);
        }}
      />
    </Card>
  );
}

function Line({ n, label, outcome, action }: { n: number; label: string; outcome: Outcome; action?: ReactNode }) {
  return (
    <ListItem
      secondaryAction={
        <Stack direction="row" sx={{ alignItems: 'center', gap: 1 }}>
          {action}
          <CheckStatus outcome={outcome} />
        </Stack>
      }
    >
      <ListItemText primary={`${n}. ${label}`} />
    </ListItem>
  );
}
