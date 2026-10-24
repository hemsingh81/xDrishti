import { Alert, Box, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatIstDayAndTime, formatWeekdayDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { accountNamesQuery, autonomyQuery, planQuery, reviewQuery, type PlanItem } from '../api/planQueries';
import { useDecisions, type Notice } from '../hooks/useDecisions';
import { lapseWording, noOrderWording } from '../lib/planText';
import { Countdown } from './Countdown';
import { PlanFlags } from './PlanFlags';
import { TicketCard } from './TicketCard';

const groups: { key: string; title: string; hint: string }[] = [
  { key: 'primary', title: 'Primary', hint: 'Tickets proposed for your decision.' },
  { key: 'reserve', title: 'Reserve', hint: 'Not promoted by anything; approve one by one only if you choose.' },
  { key: 'rejected', title: 'Rejected', hint: 'Candidates the plan turned down, with the reasons.' },
];

/** One plan: basis, cutoff, tickets by group with their evidence and checks, and the owner's decisions. */
export function PlanDetail({ planId }: { planId: number }) {
  const plan = useQuery(planQuery(planId));
  const review = useQuery(reviewQuery(planId));
  const names = useQuery(accountNamesQuery);
  const autonomy = useQuery(autonomyQuery);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [confirmAll, setConfirmAll] = useState(false);
  const decisions = useDecisions(planId, setNotice);

  if (plan.isPending || review.isPending) return <LoadingState label="Loading plan…" />;
  if (plan.isError) return <ErrorState error={plan.error} />;
  if (review.isError) return <ErrorState error={review.error} />;

  const p = plan.data;
  const rv = review.data;
  const nameMap = names.data ?? {};
  const planOpen = ['published', 'inreview'].includes(p.state.toLowerCase());
  const eligible = rv.tickets.filter((t) => t.approveAllEligible).length;
  const itemsOf = (key: string): PlanItem[] => p.items.filter((i) => i.group === key);

  return (
    <Stack spacing={2}>
      <Card component="section" aria-labelledby="plan-head">
        <CardContent>
          <Stack spacing={1}>
            <Typography id="plan-head" variant="h2">
              Plan for {formatWeekdayDate(p.forDate)} (version {p.version})
            </Typography>
            <PlanFlags plan={p} />
            <Typography variant="body2">
              <strong>Basis:</strong> {p.basis}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {p.limits}
            </Typography>
            <Typography variant="body2">
              Review cutoff {formatIstDayAndTime(rv.reviewCutoff)} IST: <Countdown cutoff={rv.reviewCutoff} />.{' '}
              {lapseWording} Decisions are {rv.open ? 'open' : 'closed'}.
            </Typography>
            <Typography variant="body2">
              Approval is manual. Automatic approval:{' '}
              {autonomy.data
                ? autonomy.data.autoApproveTickets
                  ? 'ON (set elsewhere by the owner; not changeable here)'
                  : 'OFF'
                : autonomy.isError
                  ? 'not available'
                  : 'loading'}
              . Reserve tickets are never promoted automatically.
            </Typography>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              {noOrderWording} Paper accounts only.
            </Typography>
          </Stack>
        </CardContent>
      </Card>

      {notice && (
        <Alert
          severity={notice.tone}
          onClose={() => {
            setNotice(null);
          }}
        >
          {notice.text}
          {notice.ignored && notice.ignored.length > 0 && (
            <Box component="ul" aria-label="Ignored confirmations" sx={{ m: 0, pl: 2 }}>
              <li>These confirmations covered nothing:</li>
              {notice.ignored.map((x) => (
                <li key={x}>{x}</li>
              ))}
            </Box>
          )}
          {notice.skipped && notice.skipped.length > 0 && (
            <Box component="ul" aria-label="Skipped tickets" sx={{ m: 0, pl: 2 }}>
              <li>Left for you:</li>
              {notice.skipped.map((x) => (
                <li key={x}>{x}</li>
              ))}
            </Box>
          )}
        </Alert>
      )}

      <Stack direction="row" sx={{ alignItems: 'center', flexWrap: 'wrap', gap: 1.5 }}>
        <Button
          variant="contained"
          disabled={!rv.open || eligible === 0 || decisions.busy}
          onClick={() => {
            setConfirmAll(true);
          }}
        >
          Approve all
        </Button>
        <Typography variant="body2" color="text.secondary">
          {rv.open
            ? `${eligible} primary ticket(s) have every check passed. Reserve tickets are not included.`
            : 'Decisions are closed.'}
        </Typography>
      </Stack>

      {groups.map((g) => {
        const items = itemsOf(g.key);
        return (
          <Box key={g.key} component="section" aria-label={`${g.title} tickets`}>
            <Typography variant="h3" component="h3" sx={{ mb: 0.5 }}>
              {g.title} ({items.length})
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              {g.hint}
            </Typography>
            {items.length === 0 && <Typography>None.</Typography>}
            <Stack spacing={1.5}>
              {items.map((item) => (
                <TicketCard
                  key={item.id}
                  item={item}
                  ticket={rv.tickets.find((t) => t.itemId === item.id)}
                  reviewOpen={rv.open}
                  planOpen={planOpen}
                  names={nameMap}
                  decisions={decisions}
                />
              ))}
            </Stack>
          </Box>
        );
      })}

      <ConfirmDialog
        open={confirmAll}
        title="Approve all"
        confirmLabel={`Approve ${eligible} ticket(s)`}
        busy={decisions.busy}
        onCancel={() => {
          setConfirmAll(false);
        }}
        onConfirm={() => {
          decisions.all.mutate(undefined, {
            onSettled: () => {
              setConfirmAll(false);
            },
          });
        }}
      >
        <p>
          This approves exactly {eligible} primary ticket(s) whose checks all passed. Reserve tickets are not included,
          and no ticket is approved despite a check.
        </p>
        <p>{noOrderWording}</p>
      </ConfirmDialog>
    </Stack>
  );
}
