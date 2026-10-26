import { Box } from '@mui/material';
import { MetricTile, metricOf } from '@/shared/analytics';
import { formatInr, formatPercent } from '@/shared/lib/format';
import { toMetric } from '../lib/metric';
import type { PaperAccount } from '../api/paperQueries';

const signedInr = (value: number) => `${value > 0 ? '+' : ''}${formatInr(value)}`;

/** The five headline figures of a paper account. A figure that is not a value shows its word and reason, never a digit. */
export function AccountTiles({ account }: { account: PaperAccount }) {
  const { realised } = account;
  return (
    <Box
      sx={{
        display: 'grid',
        gap: 2,
        mb: 2,
        gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(5, 1fr)' },
      }}
    >
      <MetricTile label="Equity" state={toMetric(account.equity)} format={formatInr} />
      <MetricTile
        label={realised.charges === null ? 'Result (gross)' : 'Result after stored charges'}
        state={metricOf(realised.net)}
        format={signedInr}
        tone="gain-loss"
        hint={realised.basis}
      />
      <MetricTile
        label="Open risk"
        state={toMetric(account.openRiskPct)}
        format={(v) => `${formatPercent(v)}%`}
        hint={`${account.openCount} open`}
      />
      <MetricTile label="Funds available" state={toMetric(account.fundsAvailable)} format={formatInr} />
      <MetricTile
        label="Last-session result"
        state={metricOf(account.lastSessionResult)}
        format={signedInr}
        tone="gain-loss"
        hint={<LastPct account={account} />}
      />
    </Box>
  );
}

function LastPct({ account }: { account: PaperAccount }) {
  const pct = account.lastSessionPct;
  return pct.state === 'value' && pct.number !== null
    ? `${pct.number > 0 ? '+' : ''}${formatPercent(pct.number)}% of equity at the session start`
    : `percent: ${pct.state.replace('_', ' ')}${pct.reason ? `, ${pct.reason}` : ''}`;
}
