import { Alert, Card, CardContent, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatPercent } from '@/shared/lib/format';
import { ErrorState, Facts, LoadingState, type Fact } from '@/shared/ui';
import { replayPairQuery } from '../../api/replayApi';

const pct = (v: number | null) => (v === null ? 'n/a' : `${formatPercent(v * 100)}%`);
/** A daily difference of equity, a fraction: shown as a percent with three decimals so a small edge is not rounded away. */
const daily = (v: number | null) => (v === null ? 'n/a' : `${(v * 100).toFixed(3)}%`);

const answers: Record<string, string> = {
  learning_better: 'Learning did better than frozen on the informative days.',
  frozen_better: 'Frozen did better than learning on the informative days.',
  no_measurable_difference: 'No measurable difference between learning and frozen.',
  too_little_history: 'Too little history to tell learning and frozen apart.',
};

/**
 * Frozen against learning. The answer is shown in the API's words, whatever it is: "too little history" and "no measurable
 * difference" are ordinary answers, not errors. While a run is not complete there is no answer, only the reason.
 */
export function PairCard({ pairId }: { pairId: string }) {
  const pair = useQuery(replayPairQuery(pairId));
  const d = pair.data;
  const facts: Fact[] = d
    ? [
        { label: 'Days', value: d.days ?? 'n/a' },
        { label: 'Informative days (n)', value: d.informativeDays ?? 'n/a' },
        { label: 'Probability learning is better', value: pct(d.probabilityLearningBetter) },
        { label: 'Probability frozen is better', value: pct(d.probabilityFrozenBetter) },
        { label: 'Mean daily difference (learning less frozen)', value: daily(d.meanDailyDifference) },
        {
          label: 'Interval of the daily difference',
          value: d.low === null || d.high === null ? 'n/a' : `${daily(d.low)} to ${daily(d.high)}`,
        },
      ]
    : [];
  return (
    <Card component="section" aria-labelledby="replay-pair-title">
      <CardContent sx={{ display: 'grid', gap: 1.25 }}>
        <Typography id="replay-pair-title" variant="h2" component="h2">
          Frozen against learning
        </Typography>
        {pair.isPending && <LoadingState label="Loading the comparison…" />}
        {pair.isError && <ErrorState error={pair.error} />}
        {d &&
          (d.answer === null ? (
            <Alert severity="info">{d.reason ?? 'There is no answer yet: a run is not complete.'}</Alert>
          ) : (
            <>
              <Typography variant="h3" component="p" sx={{ fontWeight: 600 }}>
                {answers[d.answer] ?? d.answer}
              </Typography>
              {d.reason && <Typography variant="body2">{d.reason}</Typography>}
              <Facts items={facts} />
            </>
          ))}
      </CardContent>
    </Card>
  );
}
