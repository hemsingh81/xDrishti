import { formatCount, formatR, metricOf, MetricValue } from '@/shared/analytics';

/** A mean R with the number of trades behind it; a missing figure is a word, and no figure is printed without its n. */
export function RValue({ value, n, missing }: { value: number | null | undefined; n?: number; missing: string }) {
  return (
    <span>
      <MetricValue
        state={metricOf(value, missing)}
        format={(v) => (n === undefined ? formatR(v) : `${formatR(v)} (n ${formatCount(n)})`)}
      />
    </span>
  );
}
