import type { IncomeView } from '@/shared/api';
import { formatInrPrecise } from '@/shared/lib/format';
import type { Fact } from '@/shared/ui';
import { costsHint, coverageHint, dividendsHint, ledgerChargesNote } from '../../lib/income';
import { NetXirr, ReturnPct } from './IncomeFigures';

/**
 * The income rows of a holding's facts: dividends and charges with the day each is counted from, the total return that includes
 * the dividends, and the net total return and net XIRR that also take the charges off. None when nothing is recorded for the
 * holding (the Dividends section below says so and offers Add dividend).
 */
export function incomeFacts(income: IncomeView | null | undefined): Fact[] {
  if (!income) return [];
  const accountCosts = ledgerChargesNote(income, formatInrPrecise);
  return [
    { label: 'Dividends', value: `${formatInrPrecise(income.dividends)} (${dividendsHint(income)})` },
    {
      label: 'Charges',
      value: `${formatInrPrecise(income.charges)} (${[accountCosts, costsHint(income)].filter(Boolean).join(' · ')})`,
    },
    { label: 'Total return (with dividends)', value: <ReturnPct value={income.totalReturnPct} /> },
    { label: 'Net total return', value: <ReturnPct value={income.netTotalReturnPct} /> },
    { label: 'Net XIRR', value: <NetXirr income={income} /> },
    { label: 'Net figures', value: coverageHint(income) },
  ];
}
