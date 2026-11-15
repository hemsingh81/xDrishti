import CloseIcon from '@mui/icons-material/Close';
import { Box, Drawer, IconButton, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { formatDate, formatInr, formatIstTime, formatPrice } from '@/shared/lib/format';
import type { SimDaySnapshot } from '../../api/simDaysApi';
import { HindsightBanner } from '../replay/HindsightBanner';

function Section({ title, count, children }: { title: string; count: number; children: ReactNode }) {
  return (
    <Box component="section" sx={{ mb: 2 }}>
      <Typography variant="h3" component="h3" sx={{ fontSize: '1rem', mb: 0.5 }}>
        {`${title} (${count})`}
      </Typography>
      {count === 0 ? (
        <Typography variant="body2" color="text.secondary">
          None.
        </Typography>
      ) : (
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {children}
        </Box>
      )}
    </Box>
  );
}

/** One stored day: its trades, picks, blocked with reasons and fills. */
export function SimDayDrawer({ day, onClose }: { day: SimDaySnapshot | null; onClose: () => void }) {
  return (
    <Drawer
      anchor="right"
      open={day !== null}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 480 }, p: 2 } } }}
    >
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h2" component="h2">
          {day ? `Day ${formatDate(day.session)}` : 'Day'}
        </Typography>
        <IconButton aria-label="Close the day" onClick={onClose} sx={{ minWidth: 44, minHeight: 44 }}>
          <CloseIcon />
        </IconButton>
      </Stack>
      {day && (
        <Box>
          <HindsightBanner run={day} />
          <Typography variant="body2" sx={{ my: 1.5 }}>
            {`Closing equity ${formatInr(day.equityClose)} · ${day.openPositions} open · ${day.costsLabel}`}
          </Typography>
          <Section title="Trades" count={day.trades.length}>
            {day.trades.map((t) => (
              <li key={t.tradeNo}>
                {`${t.symbol} ${t.side} ${t.qty} at ${formatPrice(t.entryPrice)} (${formatIstTime(t.entryAt)}), stop ${formatPrice(t.stopPrice)}${t.grossPnl === null ? ', open' : `, ${formatInr(t.grossPnl)}`}`}
              </li>
            ))}
          </Section>
          <Section title="Picks" count={day.picks.length}>
            {day.picks.map((c) => (
              <li
                key={`${c.symbol}-${c.cellKey}-${c.rank}`}
              >{`${c.symbol} ${c.side} · ${c.cellKey} · rank ${c.rank}`}</li>
            ))}
          </Section>
          <Section title="Blocked" count={day.blocked.length}>
            {day.blocked.map((b) => (
              <li key={`${b.symbol}-${b.cellKey}`}>{`${b.symbol}: ${b.reason}${b.detail ? ` — ${b.detail}` : ''}`}</li>
            ))}
          </Section>
          <Section title="Fills" count={day.fills.length}>
            {day.fills.map((f) => (
              <li key={`${f.symbol}-${f.cellKey}`}>{`${f.symbol} ${f.side} ${f.status} · ${f.qty} shares`}</li>
            ))}
          </Section>
          {day.notes.map((note) => (
            <Typography key={note} variant="body2" color="text.secondary">
              {note}
            </Typography>
          ))}
        </Box>
      )}
    </Drawer>
  );
}
