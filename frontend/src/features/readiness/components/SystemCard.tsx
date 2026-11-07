import { Typography } from '@mui/material';
import { formatCount, MetricValue, metricOf } from '@/shared/analytics';
import { Facts } from '@/shared/ui/Facts';
import type { SystemScorecard } from '../api/readinessQueries';
import { formatScore } from '../lib/words';
import { Panel } from './Panel';
import { LabelChip } from './StatusChips';

/** The system card: the minimum of the integrity and the weighted mean of the strategy scores. Derived, read only. */
export function SystemCard({ system }: { system: SystemScorecard }) {
  return (
    <Panel title="System readiness">
      <Facts
        items={[
          {
            label: 'System score',
            value: (
              <>
                <MetricValue state={metricOf(system.score, 'The score is not a number.')} format={formatScore} /> of 100{' '}
                <LabelChip code={system.label} text={system.labelText} />
              </>
            ),
          },
          {
            label: 'Integrity',
            value: (
              <MetricValue state={metricOf(system.integrity, 'The integrity is not a number.')} format={formatScore} />
            ),
          },
          {
            label: 'Weighted mean of the strategy scores',
            value: (
              <>
                <MetricValue state={metricOf(system.weightedMean, 'The mean is not a number.')} format={formatScore} />{' '}
                over {formatCount(system.strategies)} {system.strategies === 1 ? 'strategy' : 'strategies'}
                {system.equalWeights ? ' (equal weights: the plan exposes no risk share per strategy)' : ''}
              </>
            ),
          },
          {
            label: 'Weakest strategy',
            value: system.weakestStrategy
              ? `${system.weakestStrategy}${system.weakestSubScore ? `, weakest link: ${system.weakestSubScore}` : ''}`
              : 'None',
          },
          ...(system.reason ? [{ label: 'Why it reads this way', value: system.reason }] : []),
        ]}
      />
      <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>
        The system score is the minimum of the integrity and the weighted mean, so one weak link caps it. Read only: it
        raises no size and places no order.
      </Typography>
    </Panel>
  );
}
