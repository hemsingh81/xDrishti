import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatDate, formatInr } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { paperEquityQuery } from '../api/paperQueries';

const W = 600;
const H = 120;
const PAD = 6;

/** The realised-equity line: a plain SVG polyline over the sessions whose equity is a value (others are left out, not zero). */
export function EquityLine({ accountId }: { accountId: string }) {
  const equity = useQuery(paperEquityQuery(accountId));
  return (
    <Box component="section" aria-labelledby="paper-equity-heading">
      <Typography variant="h2" id="paper-equity-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
        Realised equity
      </Typography>
      {equity.isPending && <LoadingState label="Loading the equity line…" />}
      {equity.isError && <ErrorState error={equity.error} />}
      {equity.data && <Line points={equity.data.points} />}
    </Box>
  );
}

function Line({ points }: { points: { date: string; equity: { state: string; number: number | null } }[] }) {
  const known = points.flatMap((p) =>
    p.equity.state === 'value' && p.equity.number !== null ? [[p.date, p.equity.number] as const] : [],
  );
  const first = known[0];
  const last = known[known.length - 1];
  if (!first || !last) return <Typography>No equity line yet: no session has a stated equity.</Typography>;
  const values = known.map(([, v]) => v);
  const lo = Math.min(...values);
  const span = Math.max(...values) - lo || 1;
  const x = (i: number) => PAD + (known.length === 1 ? (W - 2 * PAD) / 2 : (i * (W - 2 * PAD)) / (known.length - 1));
  const y = (v: number) => H - PAD - ((v - lo) / span) * (H - 2 * PAD);
  const path = known.map(([, v], i) => `${x(i)},${y(v)}`).join(' ');
  const summary = `Realised equity from ${formatInr(first[1])} on ${formatDate(first[0])} to ${formatInr(last[1])} on ${formatDate(last[0])}, ${known.length} sessions`;
  return (
    <>
      <Box
        component="svg"
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label={summary}
        sx={{ width: '100%', maxWidth: 720, height: 'auto', color: 'primary.main' }}
      >
        <polyline points={path} fill="none" stroke="currentColor" strokeWidth={2} vectorEffect="non-scaling-stroke" />
      </Box>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 0 }}>
        {summary}.
      </Typography>
    </>
  );
}
