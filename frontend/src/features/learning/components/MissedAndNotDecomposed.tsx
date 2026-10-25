import { Box, Typography } from '@mui/material';
import { formatCount, formatR, MetricValue } from '@/shared/analytics';
import type { PlanVsActual } from '../api/learningQueries';
import { metricStateOf } from '../lib/metricView';

const stepNames: Record<string, string> = {
  exit_slippage: 'Exit slippage',
  overrides: 'Effect of the owner’s overrides',
};

/** What is outside the chain: triggered tickets that had no fill, and the parts that are never given a number. */
export function MissedAndNotDecomposed({ view }: { view: PlanVsActual }) {
  return (
    <Box sx={{ display: 'grid', gap: 1.5 }}>
      <Typography variant="body2">
        Missed triggers (approved, triggered on plan prices, no fill): {formatCount(view.missed.count)} ticket(s), total
        plan R <MetricValue state={metricStateOf(view.missed.totalPlanR)} format={formatR} />. Outside the chain above.
      </Typography>
      <Box component="ul" aria-label="Not decomposed" sx={{ m: 0, pl: 2.5 }}>
        {view.notDecomposed.map((part) => (
          <li key={part.step}>
            <strong>{stepNames[part.step] ?? part.step}: not decomposed.</strong> {part.reason}
          </li>
        ))}
      </Box>
      <Typography variant="body2" color="text.secondary">
        Not built: monthly execution charts and a fill-rate table (they need fills over months).
      </Typography>
    </Box>
  );
}
