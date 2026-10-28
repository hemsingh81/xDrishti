import CheckIcon from '@mui/icons-material/CheckCircleOutlineOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import HourglassIcon from '@mui/icons-material/HourglassEmptyOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Chip, Stack, Typography } from '@mui/material';
import { formatPrice } from '@/shared/lib/format';
import type { CaptureTicket } from '../api/captureQueries';
import { flagLabels, outcomeLabels, protectionLabels } from '../lib/captureLabels';

const icons = { success: CheckIcon, warning: WarningIcon, error: ErrorIcon, default: HelpIcon } as const;

function Outcome({ outcome }: { outcome: string | null }) {
  if (outcome === null) {
    return <Chip size="small" variant="outlined" icon={<HourglassIcon />} label="Pending the settle" />;
  }
  const known = outcomeLabels[outcome] ?? { label: outcome, tone: 'default' as const };
  const Icon = icons[known.tone];
  return <Chip size="small" variant="outlined" color={known.tone} icon={<Icon />} label={known.label} />;
}

/** A quantity from the order book, or the word when there is no order evidence: never a number for a non-value. */
const ordered = (t: CaptureTicket) =>
  t.orderEvidence === 'unavailable' ? 'unavailable' : t.orderedQty === null ? 'no order' : String(t.orderedQty);

/** One row per approved live ticket: what was allocated, ordered and filled, the outcome and its flags. Text only; the broker's reject text is data. */
export function CaptureTickets({ tickets }: { tickets: readonly CaptureTicket[] }) {
  if (tickets.length === 0) return <Typography>No approved live ticket for this day.</Typography>;
  return (
    <table aria-label="Approved live tickets">
      <thead>
        <tr>
          <th scope="col">Symbol</th>
          <th scope="col">Side</th>
          <th scope="col">Allocated</th>
          <th scope="col">Ordered</th>
          <th scope="col">Filled</th>
          <th scope="col">Average entry</th>
          <th scope="col">Outcome</th>
          <th scope="col">Flags</th>
          <th scope="col">Stop</th>
        </tr>
      </thead>
      <tbody>
        {tickets.map((t) => (
          <tr key={t.itemId}>
            <th scope="row">{t.symbol}</th>
            <td>{t.side}</td>
            <td>{t.allocatedQty}</td>
            <td>{ordered(t)}</td>
            <td>{t.filledQty}</td>
            <td>{t.avgEntry === null ? 'no fill' : formatPrice(t.avgEntry)}</td>
            <td>
              <Stack sx={{ gap: 0.5 }}>
                <Outcome outcome={t.outcome} />
                {t.missedR !== null && (
                  <Typography variant="body2">{`Plan grade ${t.missedR.toFixed(2)} R missed`}</Typography>
                )}
                {t.rejectText !== null && <Typography variant="body2">{`Dhan said: ${t.rejectText}`}</Typography>}
              </Stack>
            </td>
            <td>{t.flags.length === 0 ? 'none' : t.flags.map((f) => flagLabels[f] ?? f).join(', ')}</td>
            <td>{protectionLabels[t.protection] ?? t.protection}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
