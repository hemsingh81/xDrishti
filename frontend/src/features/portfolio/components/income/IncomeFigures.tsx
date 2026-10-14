import type { IncomeView } from '@/shared/api';
import { directionOf, signedPercent } from '../../lib/figures';
import { netXirrOf } from '../../lib/income';
import { Trend, Unavailable } from '../Figures';

const noReturnReason =
  'There is no invested capital or no price yet for these holdings, so a return in percent cannot be worked out.';

/** A return in percent with sign and arrow; "—" with the reason when there is none. */
export function ReturnPct({ value }: { value: number | null }) {
  if (value === null) return <Unavailable label="—" reason={noReturnReason} focusable />;
  return <Trend direction={directionOf(value, 2)}>{signedPercent(value)}</Trend>;
}

export function NetXirr({ income }: { income: IncomeView }) {
  const figure = netXirrOf(income);
  if ('value' in figure) return <Trend direction={directionOf(figure.value, 2)}>{signedPercent(figure.value)}</Trend>;
  return <Unavailable label={figure.label} reason={figure.reason} focusable />;
}
