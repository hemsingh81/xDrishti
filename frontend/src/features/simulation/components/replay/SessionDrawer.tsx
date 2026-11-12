import CloseIcon from '@mui/icons-material/Close';
import { Box, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import type { Schemas } from '@/shared/api';
import { formatDate, formatInr, formatPrice } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { replaySessionQuery } from '../../api/replayApi';

type Candidate = Schemas['ReplayCandidateView'];

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

const candidate = (c: Candidate) => (
  <li key={`${c.symbol}-${c.cellKey}-${c.rank}`}>{`${c.symbol} ${c.side} · ${c.cellKey} · rank ${c.rank}`}</li>
);

/** One day's plan: candidates, taken, blocked with their reasons, fills and the book open at the close. */
export function SessionDrawer({
  runId,
  session,
  onClose,
}: {
  runId: string;
  session: string | null;
  onClose: () => void;
}) {
  const query = useQuery({ ...replaySessionQuery(runId, session ?? ''), enabled: session !== null });
  const day = query.data;
  return (
    <Drawer
      anchor="right"
      open={session !== null}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 480 }, p: 2 } } }}
    >
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="h2" component="h2">
          {session ? `Plan of ${formatDate(session)}` : 'Plan'}
        </Typography>
        <IconButton aria-label="Close the day's plan" onClick={onClose} sx={{ minWidth: 44, minHeight: 44 }}>
          <CloseIcon />
        </IconButton>
      </Stack>
      {query.isPending && <LoadingState label="Loading the day…" />}
      {query.isError && <ErrorState error={query.error} />}
      {day && (
        <Box>
          <Typography variant="body2" sx={{ mb: 1.5 }}>
            {`Equity ${formatInr(day.equity)} · ${day.costsLabel}`}
          </Typography>
          <Section title="Candidates" count={day.candidates.length}>
            {day.candidates.map(candidate)}
          </Section>
          <Section title="Taken" count={day.taken.length}>
            {day.taken.map(candidate)}
          </Section>
          <Section title="Blocked" count={day.blocked.length}>
            {day.blocked.map((b) => (
              <li key={`${b.symbol}-${b.cellKey}`}>{`${b.symbol}: ${b.reason}${b.detail ? ` — ${b.detail}` : ''}`}</li>
            ))}
          </Section>
          <Section title="Fills" count={day.fills.length}>
            {day.fills.map((f) => (
              <li key={`${f.symbol}-${f.cellKey}`}>
                {`${f.symbol} ${f.side} ${f.status} · ${f.qty} shares${f.entryPrice === null ? '' : ` at ${formatPrice(f.entryPrice)}`}`}
              </li>
            ))}
          </Section>
          <Section title="Open book" count={day.openBook.length}>
            {day.openBook.map((p) => (
              <li key={`${p.symbol}-${p.entrySession}`}>
                {`${p.symbol} ${p.side} · ${p.qty} shares at ${formatPrice(p.entryPrice)}, stop ${formatPrice(p.stopPrice)}`}
              </li>
            ))}
          </Section>
          {!day.openBookComplete && (
            <Typography variant="body2" color="text.secondary">
              The open book is not complete while the run is active.
            </Typography>
          )}
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
