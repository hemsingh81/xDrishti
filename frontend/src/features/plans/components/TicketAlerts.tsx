import { Alert, Button, Stack, Typography } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { formatIstTime, formatPrice } from '@/shared/lib/format';
import { muteAlerts, planKeys, type Crossing, type PlanAlerts, type Prompt } from '../api/planQueries';
import { alertError } from '../lib/alertText';
import { AlertNoticeControls } from './AlertNoticeControls';

const alertOnlyWording = 'Alert only: no order is placed.';

const kindWords: Record<string, string> = { stop_crossed: 'stop crossed', target_crossed: 'target crossed' };

const promptWords: Record<string, string> = {
  open: 'Post-trade prompt: open, waiting for your journal entry or a trade on record.',
  expired_no_fill: 'Post-trade prompt: expired, no fill was found.',
  ambiguous: 'Post-trade prompt: ambiguous, the trade could belong to more than one ticket.',
};

function promptLine(p: Prompt, itemNames: Record<number, string>): string {
  if (p.status === 'matched') return `Post-trade prompt: matched (via ${p.via ?? 'unknown'}).`;
  const line = promptWords[p.status] ?? `Post-trade prompt: ${p.status}.`;
  if (p.status !== 'ambiguous' || p.sameTradeItems.length === 0) return line;
  return `${line} Other tickets: ${p.sameTradeItems.map((i) => itemNames[i] ?? `#${String(i)}`).join(', ')}.`;
}

const untilText = (until: string | null) => (until ? ` (snoozed until ${formatIstTime(until)} IST)` : '');
const live = (state: string) => state === 'shown' || state === 'snoozed';

interface Props {
  planId: number;
  itemId: number;
  alerts: PlanAlerts;
  /** Symbols of the plan's tickets by item id, to name the other tickets of an ambiguous trade. */
  itemNames: Record<number, string>;
}

/** Price-crossing alerts and post-trade prompts of one watched ticket. Text from the server is plain text. */
export function TicketAlerts({ planId, itemId, alerts, itemNames }: Props) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const mute = useMutation({
    mutationFn: () => muteAlerts(itemId),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: planKeys.alerts(planId) }),
        queryClient.invalidateQueries({ queryKey: planKeys.notices }),
      ]),
    onError: (e) => {
      setError(alertError(e));
    },
  });
  if (!alerts.enabled) return null;

  const crossings = alerts.crossings.filter((c) => c.itemId === itemId && live(c.state));
  const prompts = alerts.prompts.filter((p) => p.itemId === itemId && live(p.state));
  const muted = alerts.mutedItems.includes(itemId);
  const fail = (e: unknown) => {
    setError(alertError(e));
  };
  const crossingLine = (c: Crossing) =>
    `Alerts: ${kindWords[c.kind] ?? c.kind} at ${formatPrice(c.price)} ${formatIstTime(c.tickAt)} IST`;

  return (
    <Stack component="section" aria-label="Ticket alerts" spacing={0.75}>
      {crossings.length === 0 && <Typography variant="body2">Alerts: none yet</Typography>}
      {crossings.map((c) => (
        <Stack key={c.noticeId} spacing={0.5} sx={{ opacity: c.state === 'snoozed' ? 0.7 : 1 }}>
          <Typography variant="body2">
            {crossingLine(c)}
            {c.state === 'snoozed' ? untilText(c.snoozedUntil) : ''}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {c.text}
          </Typography>
          <AlertNoticeControls
            planId={planId}
            noticeId={c.noticeId}
            label={kindWords[c.kind] ?? c.kind}
            snoozesLeft={c.snoozesLeft}
            onError={fail}
          />
        </Stack>
      ))}
      {prompts.map((p) => (
        <Stack key={p.noticeId} spacing={0.5} sx={{ opacity: p.state === 'snoozed' ? 0.7 : 1 }}>
          <Typography variant="body2">
            {promptLine(p, itemNames)}
            {p.state === 'snoozed' ? untilText(p.snoozedUntil) : ''}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {p.text}
          </Typography>
          <AlertNoticeControls
            planId={planId}
            noticeId={p.noticeId}
            label={`post-trade prompt ${p.status}`}
            snoozesLeft={p.snoozesLeft}
            onError={fail}
          />
        </Stack>
      ))}
      <Stack direction="row" sx={{ alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
        <Button
          size="small"
          variant="outlined"
          disabled={muted || mute.isPending}
          onClick={() => {
            setError(null);
            mute.mutate();
          }}
        >
          Mute alerts
        </Button>
        {muted && <Typography variant="body2">Alerts muted for this ticket.</Typography>}
        <Typography variant="caption" color="text.secondary">
          {alertOnlyWording}
        </Typography>
      </Stack>
      {error && <Alert severity="error">{error}</Alert>}
    </Stack>
  );
}
