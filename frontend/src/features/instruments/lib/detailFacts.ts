import type { InstrumentDetail } from '@/shared/api';
import { formatDate, formatDateTime, formatNumber } from '@/shared/lib/format';
import type { Fact } from '@/shared/ui';
import { sectorNone, typeLabel } from './labels';

const dash = '—';
const orDash = (value: string | null): string => value ?? dash;
const yesNo = (value: boolean): string => (value ? 'Yes' : 'No');

const sectorSources: Readonly<Record<string, string>> = {
  nse: 'NSE Nifty Total Market list',
  manual: 'Entered manually',
};

function sectorText(detail: InstrumentDetail): string {
  if (!detail.sector) return `${sectorNone} (no sector known)`;
  const source = detail.sectorSource ? (sectorSources[detail.sectorSource.toLowerCase()] ?? detail.sectorSource) : null;
  return source ? `${detail.sector} — from ${source}` : detail.sector;
}

function asmGsmText(detail: InstrumentDetail): string {
  if (!detail.asmGsm) return 'No';
  const parts = [detail.asmGsmFlag, detail.asmGsmCategory].filter((p): p is string => Boolean(p));
  return parts.length > 0 ? `Yes — ${parts.join(' · ')}` : 'Yes';
}

/** Label/value rows for the detail drawer: every field of the instrument, in reading order. */
export function detailFacts(d: InstrumentDetail): Fact[] {
  return [
    { label: 'Symbol', value: d.symbol },
    { label: 'Name', value: d.name },
    { label: 'Display name', value: d.displayName },
    { label: 'Type', value: typeLabel(d.type) },
    { label: 'ISIN', value: orDash(d.isin) },
    { label: 'Series', value: orDash(d.series) },
    { label: 'Sector', value: sectorText(d) },
    { label: 'Underlying', value: d.underlyingSymbol },
    { label: 'Expiry', value: d.expiryDate ? formatDate(d.expiryDate) : dash },
    { label: 'Exchange', value: `${d.exchange} · ${d.exchangeSegment}` },
    { label: 'Lot size', value: formatNumber(d.lotSize) },
    { label: 'Tick size', value: d.tickSize === null ? dash : formatNumber(d.tickSize) },
    { label: 'F&O eligible', value: yesNo(d.fnoEligible) },
    { label: 'ASM/GSM', value: asmGsmText(d) },
    { label: 'MTF leverage', value: d.mtfLeverage === null ? dash : `${formatNumber(d.mtfLeverage)}x` },
    { label: 'Freeze quantity', value: d.freezeQuantity === null ? dash : formatNumber(d.freezeQuantity) },
    {
      label: 'Dhan id',
      value: d.brokerIds.length === 0 ? dash : d.brokerIds.map((b) => `${b.brokerId} (${b.segment})`).join(', '),
    },
    { label: 'First seen', value: formatDateTime(d.firstSeenAt) },
    { label: 'Last seen', value: formatDateTime(d.lastSeenAt) },
    ...(d.retiredAt ? [{ label: 'Retired', value: formatDateTime(d.retiredAt) }] : []),
  ];
}
