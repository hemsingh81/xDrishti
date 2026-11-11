import { NonValue } from '@/shared/analytics';
import { TimeSeriesChart } from '@/shared/ui/chart';
import type { BasketProfile } from '../api/profileQueries';
import { ratioOf } from '../lib/format';
import { Section } from './Section';

/** Beta against the index over the last 60 common sessions, day by day; a window with no variance in the index has no point. */
export function RollingBetaChart({ profile }: { profile: BasketProfile }) {
  const { series, risk } = profile;
  const any = series.rollingBeta60.some((v) => typeof v === 'number');
  if (!any) {
    return (
      <Section title="Rolling beta, 60 sessions">
        <NonValue
          state={
            risk.beta60.state === 'value'
              ? { kind: 'unavailable', reason: 'No window of 60 common sessions has an index that moves.' }
              : { kind: 'unavailable', reason: risk.beta60.reason ?? 'Beta needs an index with daily bars.' }
          }
        />
      </Section>
    );
  }
  return (
    <Section
      title="Rolling beta, 60 sessions"
      note="How strongly the basket moved with the index over each 60 common sessions."
    >
      <TimeSeriesChart
        label="Rolling beta against the index"
        series={[
          {
            id: 'beta',
            name: 'Beta, 60 sessions',
            color: 'info',
            data: series.dates.map((d, i) => [d, series.rollingBeta60[i] ?? null]),
          },
        ]}
        valueFormatter={ratioOf}
        height={220}
      />
    </Section>
  );
}
