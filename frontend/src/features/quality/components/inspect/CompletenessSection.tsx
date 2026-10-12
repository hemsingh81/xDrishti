import { Typography } from '@mui/material';
import type { CompletenessCheck, QualityMissing } from '@/shared/api';
import { formatDate, formatNumber, formatPercent } from '@/shared/lib/format';
import { checkTitles } from '../../lib/labels';
import { CheckSection } from './CheckSection';
import { MiniTable } from './MiniTable';

/** Missing minutes grouped by day: { '2026-10-09': ['09:15', '09:16'] } in the order of the list. */
function groupByDate(items: QualityMissing['items']): [string, string[]][] {
  const groups = new Map<string, string[]>();
  for (const { date, time } of items) groups.set(date, [...(groups.get(date) ?? []), time]);
  return [...groups];
}

/** Completeness: the minutes the calendar expects against the stored ones, per session, and the missing minutes. */
export function CompletenessSection({ check, missing }: { check: CompletenessCheck; missing: QualityMissing }) {
  return (
    <CheckSection title={checkTitles.completeness} status={check.status}>
      <Typography>
        Expected {formatNumber(check.expectedMinutes)} one-minute bars in the window, found{' '}
        {formatNumber(check.foundMinutes)}; {formatNumber(check.missingMinutes)} missing (
        {formatPercent(check.missingPercent)} %).
      </Typography>
      {check.unfillableMinutes > 0 && (
        <Typography variant="body2" color="text.secondary">
          {formatNumber(check.unfillableMinutes)} of them are in sessions a re-fetch could not fill (a halt or a locked
          day): a warning, not a failure.
        </Typography>
      )}
      {check.sessions.length > 0 && (
        <MiniTable
          label="Sessions"
          columns={[
            { header: 'Session' },
            { header: 'Expected', numeric: true },
            { header: 'Found', numeric: true },
            { header: 'Missing', numeric: true },
            { header: 'Unfillable' },
          ]}
          rows={check.sessions.map((s) => ({
            key: s.date,
            cells: [
              <time key="d" dateTime={s.date}>
                {formatDate(s.date)}
              </time>,
              formatNumber(s.expected),
              formatNumber(s.found),
              formatNumber(s.missing),
              s.unfillable ? 'Yes' : '—',
            ],
          }))}
        />
      )}
      {missing.total === 0 ? (
        <Typography variant="body2" color="text.secondary">
          No minutes are missing.
        </Typography>
      ) : (
        <>
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            Missing minutes: showing {formatNumber(missing.items.length)} of {formatNumber(missing.total)}
          </Typography>
          {groupByDate(missing.items).map(([date, times]) => (
            <Typography key={date} variant="body2" sx={{ overflowWrap: 'anywhere' }}>
              <strong>{formatDate(date)}</strong>: {times.join(', ')}
            </Typography>
          ))}
        </>
      )}
    </CheckSection>
  );
}
