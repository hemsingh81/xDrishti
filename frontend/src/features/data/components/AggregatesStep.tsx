import LayersIcon from '@mui/icons-material/LayersOutlined';
import { Box, Chip, Typography } from '@mui/material';
import type { AggregatePhase, AggregatesOverview } from '@/shared/api';
import { formatCompact, formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { isActive } from '../lib/phases';
import { aggregateResult } from '../lib/summaries';
import { PhaseNotice } from './PhaseNotice';
import { PipelineStep } from './PipelineStep';
import { StepProgress } from './StepProgress';

interface AggregatesStepProps {
  aggregates: AggregatesOverview;
  phase: AggregatePhase | null;
}

/** Step 3: the bars stored per derived timeframe, when they were last built and what is waiting to be aggregated. */
export function AggregatesStep({ aggregates, phase }: AggregatesStepProps) {
  const { timeframes, lastBuiltAt, instrumentsPending } = aggregates;
  return (
    <PipelineStep number={3} title="Aggregates" icon={LayersIcon} hue="violet" phase={phase}>
      {timeframes.length > 0 ? (
        <Box
          component="ul"
          aria-label="Bars per timeframe"
          sx={{ m: 0, p: 0, display: 'flex', flexWrap: 'wrap', gap: 0.75 }}
        >
          {timeframes.map((t) => (
            <Box component="li" key={t.timeframe} sx={{ listStyle: 'none' }}>
              <Chip size="small" variant="outlined" label={`${t.timeframe} · ${formatCompact(t.bars)}`} />
            </Box>
          ))}
        </Box>
      ) : (
        <Typography sx={{ fontWeight: 600 }}>No aggregates built yet</Typography>
      )}
      {lastBuiltAt && (
        <Typography variant="body2" color="text.secondary">
          Last built <time dateTime={lastBuiltAt}>{formatIstDateTime(lastBuiltAt)}</time>
        </Typography>
      )}
      {instrumentsPending > 0 && (
        <Typography variant="body2" color="text.secondary">
          {formatNumber(instrumentsPending)} instruments have new bars not aggregated yet
        </Typography>
      )}
      {phase && isActive(phase.status) && (
        <StepProgress
          title="Aggregates"
          status={phase.status === 'pending' ? 'pending' : 'running'}
          done={phase.done}
          total={phase.total}
          unit="instruments"
          message={phase.message}
        />
      )}
      {phase && !isActive(phase.status) && (
        <>
          {phase.status === 'failed' || phase.status === 'skipped' ? (
            <PhaseNotice status={phase.status} message={phase.message} code={phase.code} />
          ) : (
            <Typography variant="body2">{aggregateResult(phase)}</Typography>
          )}
        </>
      )}
    </PipelineStep>
  );
}
