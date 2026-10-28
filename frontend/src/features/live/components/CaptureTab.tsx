import CheckIcon from '@mui/icons-material/CheckCircleOutlineOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Alert, Chip, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { accountsQuery } from '@/features/accounts';
import { todayIso } from '@/shared/lib/dates';
import { ErrorState, LoadingState } from '@/shared/ui';
import { captureQuery, type LiveCaptureView } from '../api/captureQueries';
import { healthLabels, readOnlySentence } from '../lib/captureLabels';
import { CaptureFills } from './CaptureFills';
import { CaptureTickets } from './CaptureTickets';

const healthIcons = { success: CheckIcon, warning: WarningIcon, default: HelpIcon } as const;

function Health({ health }: { health: string }) {
  const known = healthLabels[health] ?? { label: health, tone: 'default' as const };
  const Icon = healthIcons[known.tone];
  return <Chip size="small" variant="outlined" color={known.tone} icon={<Icon />} label={`Capture: ${known.label}`} />;
}

function Evidence({ view }: { view: LiveCaptureView }) {
  const e = view.evidence;
  return (
    <section aria-label="Evidence">
      <Typography variant="h3" component="h3">
        Evidence
      </Typography>
      <Typography>
        {e.orderEvidence === 'present'
          ? `Order book read in ${String(e.pollRuns)} polls; ${String(e.fills)} fills and ${String(e.orders)} orders stored.`
          : `No order evidence for this day (the order book was not read), so every order-dependent field is unavailable. ${String(e.fills)} fills stored.`}
      </Typography>
      {!e.orderBookEnabled && (
        <Typography variant="body2" color="text.secondary">
          The order book is not read (<code>Live__Capture__OrderBook__Enabled</code> is false).
        </Typography>
      )}
      <ul>
        {e.limits.map((limit) => (
          <li key={limit}>
            <Typography variant="body2">{limit}</Typography>
          </li>
        ))}
      </ul>
    </section>
  );
}

/**
 * The Capture tab of the Live screen (spec P11-01): what you did in Dhan on a day, matched to the tickets you approved - one outcome per ticket, the fills no
 * ticket owns, and what the capture rests on. It reads only the capture API; nothing here calls a broker or places an order.
 */
export function CaptureTab() {
  const accounts = useQuery(accountsQuery);
  const [picked, setPicked] = useState<string | null>(null);
  const [date, setDate] = useState(todayIso());
  const live = (accounts.data ?? []).filter((a) => a.roles.trading.enabled && a.roles.trading.mode === 'manual_live');
  const account = live.find((a) => a.id === picked) ?? live[0];
  const q = useQuery({ ...captureQuery(account?.id ?? '', date), enabled: account !== undefined });
  if (accounts.isPending) return <LoadingState label="Loading the capture…" />;
  if (accounts.isError) return <ErrorState error={accounts.error} />;
  return (
    <Stack sx={{ gap: 2 }}>
      <Alert severity="info">{readOnlySentence}</Alert>
      {!account ? (
        <Typography>No account is in manual-live mode, so there is nothing to capture.</Typography>
      ) : (
        <>
          <Stack direction="row" sx={{ gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
            <TextField
              select
              size="small"
              label="Account"
              value={account.id}
              onChange={(e) => {
                setPicked(e.target.value);
              }}
              sx={{ minWidth: 220 }}
            >
              {live.map((a) => (
                <MenuItem key={a.id} value={a.id}>
                  {a.name}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              type="date"
              size="small"
              label="Day"
              value={date}
              onChange={(e) => {
                // The picker stops at today; a typed future date is ignored.
                if (e.target.value !== '' && e.target.value <= todayIso()) setDate(e.target.value);
              }}
              slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: todayIso() } }}
              sx={{ width: 160 }}
            />
            {q.data && <Health health={q.data.health} />}
          </Stack>
          {q.isPending && <LoadingState label="Loading the day…" />}
          {q.isError && <ErrorState error={q.error} />}
          {q.data && (
            <>
              <CaptureTickets tickets={q.data.tickets} />
              <CaptureFills
                title="Fills no ticket owns"
                fills={q.data.unmatched}
                empty="None: every fill belongs to a ticket or is your own trade."
              />
              <CaptureFills
                title="Your own trades"
                fills={q.data.discretionary}
                empty="No trade outside the approved tickets."
              />
              <Evidence view={q.data} />
            </>
          )}
        </>
      )}
    </Stack>
  );
}
