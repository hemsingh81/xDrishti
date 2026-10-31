import { Box, LinearProgress, Stack, Typography } from '@mui/material';
import { NonValue } from '@/shared/analytics';
import type { Schemas } from '@/shared/api';
import { formatNumber, formatInrPrecise, formatIstDateTime } from '@/shared/lib/format';
import { execMetric } from '../lib/metrics';

type Caps = Schemas['OverviewCaps'];

function Meter({ label, metric, money }: { label: string; metric: Schemas['ExecMetric']; money?: boolean }) {
  const state = execMetric(metric);
  const write = money ? formatInrPrecise : formatNumber;
  return (
    <Box sx={{ minWidth: 0 }}>
      <Typography variant="caption" id={`meter-${label}`} sx={{ color: 'text.secondary', fontWeight: 600 }}>
        {label}
      </Typography>
      {state.kind !== 'value' ? (
        <Box sx={{ fontSize: 14 }}>
          <NonValue state={state} />
        </Box>
      ) : metric.cap === null || metric.cap === undefined ? (
        <>
          <Typography>{write(state.value)} used</Typography>
          <Typography variant="caption" sx={{ color: 'warning.main' }}>
            No cap set: nothing may be sent automatically
          </Typography>
        </>
      ) : (
        <>
          <Typography>
            {write(state.value)} of {write(metric.cap)}
          </Typography>
          <LinearProgress
            aria-labelledby={`meter-${label}`}
            variant="determinate"
            value={metric.cap > 0 ? Math.min(100, (state.value / metric.cap) * 100) : 100}
            color={metric.cap > 0 && state.value / metric.cap >= 0.8 ? 'warning' : 'primary'}
          />
        </>
      )}
    </Box>
  );
}

/** The four meters against the limits sheet in force; the raise that waits is shown beside, never mixed in. */
export function CapsMeters({ caps }: { caps: Caps }) {
  const pending = caps.pendingLimits;
  return (
    <Stack sx={{ gap: 1.5 }}>
      <Box
        sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(4, 1fr)' } }}
      >
        <Meter label="Orders today" metric={caps.orders} />
        <Meter label="Notional today" metric={caps.notional} money />
        <Meter label="Worst-case loss" metric={caps.worstCaseLoss} money />
        <Meter label="Open positions" metric={caps.openPositions} />
      </Box>
      {pending && (
        <Typography variant="caption" sx={{ color: 'text.secondary' }}>
          Pending sheet (not in force yet), from {formatIstDateTime(pending.effectiveFrom)}: orders a day{' '}
          {pending.maxOrdersPerDay ?? 'unset'}, notional a day{' '}
          {pending.maxNotionalPerDay === null ? 'unset' : formatInrPrecise(pending.maxNotionalPerDay)}, loss a day{' '}
          {pending.maxLossPerDay === null ? 'unset' : formatInrPrecise(pending.maxLossPerDay)}.
        </Typography>
      )}
    </Stack>
  );
}
