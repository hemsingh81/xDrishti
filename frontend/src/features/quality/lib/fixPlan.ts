import type { DataOverview, QualityCounts } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

export interface FixStep {
  id: 'dhan' | 'aggregate' | 'quality';
  title: string;
  /** What the analysis found, in words. */
  detail: string;
  /** False when the step is left out (Dhan cannot be asked). */
  included: boolean;
}

export interface FixPlan {
  steps: FixStep[];
  /** True when something is wrong or waiting; false = nothing to fix. */
  needed: boolean;
  /** Whether the Dhan step is part of the job. */
  dhan: boolean;
}

const plural = (n: number, one: string, many: string) => `${formatNumber(n)} ${n === 1 ? one : many}`;

/**
 * Reads the data overview and the quality counts and says what a fix would do: fetch the sessions with missing bars from
 * Dhan (also the ones given up on), rebuild the aggregates of the instruments that are behind or failing, and check
 * everything again. Dhan is left out, with the reason, when it cannot be asked.
 */
export function planFix(
  data: Pick<DataOverview, 'dhan' | 'gaps' | 'aggregates'>,
  counts: Pick<QualityCounts, 'failed' | 'warned' | 'notChecked' | 'tracked'>,
): FixPlan {
  const issues = counts.failed + counts.warned + counts.notChecked;
  const gaps = data.gaps.open + data.gaps.unfillable;
  const waiting = data.aggregates.instrumentsPending;
  const dhan = data.dhan.canFetch;
  const steps: FixStep[] = [
    {
      id: 'dhan',
      title: 'Fetch missing bars from Dhan',
      detail: !dhan
        ? `Left out: ${data.dhan.reason ?? 'Dhan cannot be asked now.'}`
        : gaps > 0
          ? `${plural(gaps, 'session', 'sessions')} with missing bars (${formatNumber(data.gaps.unfillable)} given up on earlier are tried again).`
          : 'No sessions with missing bars are known; newer bars are fetched.',
      included: dhan,
    },
    {
      id: 'aggregate',
      title: 'Rebuild the aggregates',
      detail:
        waiting > 0
          ? `${plural(waiting, 'instrument', 'instruments')} waiting for their timeframes.`
          : 'Every timeframe is rebuilt from the 1-minute bars where it differs.',
      included: true,
    },
    {
      id: 'quality',
      title: 'Check again',
      detail: `${plural(counts.tracked, 'instrument', 'instruments')}: ${formatNumber(counts.failed)} failing, ${formatNumber(counts.warned)} with warnings, ${formatNumber(counts.notChecked)} not checked.`,
      included: true,
    },
  ];
  return { steps, needed: issues > 0 || gaps > 0 || waiting > 0, dhan };
}
