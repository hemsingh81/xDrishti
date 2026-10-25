import { Box, Typography } from '@mui/material';
import { MetricValue, NonValue } from '@/shared/analytics';
import { numeric } from '@/theme';
import type { PlanVsActual } from '../api/learningQueries';
import { formatStepR } from '../lib/formatChain';
import { metricStateOf } from '../lib/metricView';

type Step = PlanVsActual['chain'][number];

/**
 * The steps from the plan's R to the realised R, in order, each with its signed value as text. A bar is drawn only for a real
 * number; a step without one shows its word and reason and no bar at all.
 */
export function WaterfallList({ chain }: { chain: readonly Step[] }) {
  const states = chain.map((step) => metricStateOf(step.metric));
  const largest = Math.max(
    0,
    ...states.flatMap((s) => (s.kind === 'value' || s.kind === 'provisional' ? [Math.abs(s.value)] : [])),
  );
  return (
    <Box
      component="ol"
      aria-label="From plan to actual"
      sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gap: 1 }}
    >
      {chain.map((step, index) => {
        const state = states[index];
        if (!state) return null;
        const hasNumber = state.kind === 'value' || state.kind === 'provisional';
        const width = hasNumber && largest > 0 ? (Math.abs(state.value) / largest) * 100 : 0;
        return (
          <Box
            component="li"
            key={step.step}
            sx={{
              display: 'grid',
              gridTemplateColumns: { xs: '1fr', sm: '16rem 8rem 1fr' },
              gap: 1,
              alignItems: 'center',
            }}
          >
            <Typography variant="body2">{step.label}</Typography>
            <Box sx={{ ...numeric }}>
              {hasNumber ? <MetricValue state={state} format={formatStepR} /> : <NonValue state={state} />}
            </Box>
            {hasNumber && (
              <Box
                aria-hidden
                sx={{
                  height: 10,
                  borderRadius: 1,
                  width: `${width}%`,
                  minWidth: 2,
                  bgcolor: 'text.secondary',
                  opacity: 0.6,
                }}
              />
            )}
          </Box>
        );
      })}
    </Box>
  );
}
