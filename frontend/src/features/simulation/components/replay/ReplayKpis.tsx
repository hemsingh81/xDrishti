import { Box } from '@mui/material';
import { formatNumber, formatPercent } from '@/shared/lib/format';
import { StatTile } from '@/shared/ui';
import type { ReplayRun } from '../../api/replayApi';

const none = 'Not enough data';
const pct = (value: number | null | undefined, scale = 1) =>
  value === null || value === undefined ? none : `${formatPercent(value * scale)}%`;
const num = (value: number | null | undefined) => (value === null || value === undefined ? none : formatPercent(value));

const ordinal = (n: number) => {
  const s = ['th', 'st', 'nd', 'rd'];
  const v = n % 100;
  return `${formatNumber(n)}${s[(v - 20) % 10] ?? s[v] ?? s[0] ?? 'th'}`;
};

/** The figures so far (null where they cannot be told: shown as such, never as 0) and the trial number. */
export function ReplayKpis({ run }: { run: ReplayRun }) {
  const f = run.figures;
  return (
    <Box
      aria-label="Key figures"
      role="group"
      sx={{
        display: 'grid',
        gap: 1.5,
        gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' },
      }}
    >
      <StatTile label="CAGR" value={pct(f?.cagr, 100)} />
      <StatTile label="Max drawdown" value={pct(f?.maxDrawdownPct)} />
      <StatTile label="Sharpe" value={num(f?.sharpe)} />
      <StatTile label="Exposure" value={pct(f?.exposure, 100)} />
      <StatTile label="Trades" value={f ? formatNumber(f.trades) : none} />
      <StatTile label="Hit rate" value={pct(f?.hitRate, 100)} />
      <StatTile label="Mean R" value={num(f?.meanR)} />
      <StatTile
        label="Trial"
        value={ordinal(run.trialNumber)}
        hint={`The ${ordinal(run.trialNumber)} replay of this range`}
      />
    </Box>
  );
}
