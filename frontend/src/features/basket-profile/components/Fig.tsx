import { MetricValue } from '@/shared/analytics';
import { figure, type Figure } from '../lib/figure';

interface FigProps {
  view: Figure;
  format: (value: number) => string;
  tone?: 'neutral' | 'gain-loss';
}

/** One server figure in its honest state: a number only for a value, otherwise the word, the reason and no digit. */
export function Fig({ view, format, tone = 'neutral' }: FigProps) {
  return <MetricValue state={figure(view)} format={format} tone={tone} />;
}
