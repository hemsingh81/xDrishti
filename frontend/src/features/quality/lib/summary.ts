import type { QualityOverview } from '@/shared/api';
import { formatDate, formatNumber } from '@/shared/lib/format';

type Gate = QualityOverview['gate'];

export interface GateLook {
  word: string;
  tone: 'success' | 'warning' | 'error';
}

/** The gate said in words: OK, Blocked (the nightly plan does not run), or open with failing instruments. */
export function gateLook(gate: Gate): GateLook {
  if (gate.blocked) return { word: 'Blocked', tone: 'error' };
  if (gate.ok) return { word: 'OK', tone: 'success' };
  return { word: 'Open with failures', tone: 'warning' };
}

/** "6–12 Oct 2026" within one month, "29 Sep 2026 – 5 Oct 2026" across months. */
function dateRange(from: string, to: string): string {
  const [fromYear, fromMonth, fromDay] = from.split('-');
  const [toYear, toMonth] = to.split('-');
  if (fromYear === toYear && fromMonth === toMonth && fromDay) return `${String(Number(fromDay))}–${formatDate(to)}`;
  return `${formatDate(from)} – ${formatDate(to)}`;
}

/** "Last 5 sessions, 6–12 Oct 2026" */
export function windowText({ window }: Pick<QualityOverview, 'window'>): string {
  const sessions = `${formatNumber(window.sessions)} ${window.sessions === 1 ? 'session' : 'sessions'}`;
  return window.from && window.to ? `Last ${sessions}, ${dateRange(window.from, window.to)}` : `Last ${sessions}`;
}

/** "Spike above 20 × the median move · missing minutes above 1 %" */
export const limitsText = ({
  spikeMultiple,
  maxMissingPercent,
}: Pick<QualityOverview, 'spikeMultiple' | 'maxMissingPercent'>) =>
  `Spike above ${formatNumber(spikeMultiple)} × the median move · missing minutes above ${formatNumber(maxMissingPercent)} %`;
