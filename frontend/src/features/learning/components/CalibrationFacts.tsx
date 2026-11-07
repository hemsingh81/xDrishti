import { MetricValue } from '@/shared/analytics';
import { Facts } from '@/shared/ui/Facts';
import type { Model } from '../api/learningQueries';
import { figure, formatFine } from '../lib/figure';

/**
 * What the registry keeps about the model's calibration. The pooled error is a stored field; the interval, the complexity-ladder
 * rung and the calibration kind are not columns yet, so they read "Not available" with the reason, never a guess.
 */
export function CalibrationFacts({ model }: { model: Model }) {
  return (
    <Facts
      items={[
        {
          label: 'Pooled calibration error',
          value: (
            <MetricValue
              state={figure(model.pooledEcePp, 'not stored')}
              format={(v) => `${formatFine(v)} percentage points`}
            />
          ),
        },
        {
          label: 'Interval of the error',
          value: (
            <MetricValue
              state={{ kind: 'unavailable', reason: 'the registry stores the pooled error without its interval' }}
            />
          ),
        },
        {
          label: 'Calibration kind',
          value: (
            <MetricValue
              state={{
                kind: 'unavailable',
                reason:
                  'calibration_kind has no column yet; the kind (Platt or isotonic) is only in the model file header',
              }}
            />
          ),
        },
        {
          label: 'Ladder rung',
          value: (
            <MetricValue
              state={{
                kind: 'unavailable',
                reason: 'the rung is not a stored field; read it in the validation report above',
              }}
            />
          ),
        },
      ]}
    />
  );
}
