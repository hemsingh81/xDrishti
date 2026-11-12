import { Box, Chip, Typography } from '@mui/material';
import { formatIstDate, formatNumber } from '@/shared/lib/format';
import type { HistoryOverview } from '../../api/historyQueries';
import { statusLabels } from '../../lib/history';

/** The totals above the table: how many instruments, in which state, the chunks, the years of history, the daily span. */
export function HistoryTotals({ data }: { data: HistoryOverview }) {
  const { totals, daily } = data;
  const years = totals.years;
  return (
    <Box component="section" aria-label="History totals" sx={{ display: 'grid', gap: 1 }}>
      <Typography variant="body2">
        <strong>{formatNumber(totals.instruments)}</strong> tracked instruments. Chunks:{' '}
        {formatNumber(totals.chunks.done)} of {formatNumber(totals.chunks.total)} done,{' '}
        {formatNumber(totals.chunks.failed)} failed.
      </Typography>
      <Box
        component="ul"
        aria-label="Instruments by backfill status"
        sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, m: 0, p: 0, listStyle: 'none' }}
      >
        {Object.entries(totals.byStatus).map(([status, count]) => (
          <li key={status}>
            <Chip size="small" variant="outlined" label={`${statusLabels[status] ?? status}: ${formatNumber(count)}`} />
          </li>
        ))}
      </Box>
      <Typography variant="body2" color="text.secondary">
        Years of 1-minute history: under 2 years {formatNumber(years.under2)}, 2 to 3 {formatNumber(years.from2to3)}, 3
        to 5 {formatNumber(years.from3to5)}, 5 or more {formatNumber(years.from5)}.
      </Typography>
      <Typography variant="body2" color="text.secondary">
        Daily history (bhavcopy):{' '}
        {daily ? `${formatIstDate(daily.from)} to ${formatIstDate(daily.to)}` : 'no daily file is loaded yet'}. Run
        window {data.runWindow} IST, {data.inWindow ? 'open now' : 'closed now'}
        {data.running ? '; the backfill service is running' : ''}.
      </Typography>
    </Box>
  );
}
