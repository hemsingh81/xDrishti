import { Alert, Typography } from '@mui/material';
import { formatDate, formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { Facts } from '@/shared/ui';
import type { HoldOutResult } from '../api/confidenceQueries';

const num = (v: number | null, unit = ''): string => (v === null ? 'n/a' : `${formatNumber(v)}${unit}`);

/** The stored look at the hold-out. Only drawn from a result the server stored; never from anything computed here. */
export function HoldOutNumbers({ result }: { result: HoldOutResult }) {
  return (
    <>
      <Alert severity={result.passed ? 'success' : 'warning'} sx={{ my: 1 }}>
        {`Hold-out rule: ${result.status}. ${result.passed ? 'Counts toward a pass.' : 'Does not count toward a pass.'} ${result.reason}`}
        {result.reusedWindow
          ? ' This window was looked at before: it counts as a trial and cannot count toward a pass.'
          : ''}
        {result.runSawHoldOut ? ' The window of the run reached the block, so this is never a pass.' : ''}
      </Alert>
      <Facts
        items={[
          { label: 'Looked at', value: `${formatIstDateTime(result.unlockedAt)}, ${result.mode}, by ${result.actor}` },
          { label: 'Window', value: `${formatDate(result.from)} to ${formatDate(result.to)}` },
          { label: 'Hold-out trades', value: result.trades },
          { label: 'Hold-out expectancy', value: num(result.holdOutExpectancyR, ' R') },
          { label: 'Development expectancy', value: num(result.developmentExpectancyR, ' R') },
          {
            label: 'Kept of development',
            value: result.ratio === null ? 'n/a' : `${formatNumber(result.ratio * 100)} %`,
          },
        ]}
      />
      {result.notes.map((n, i) => (
        <Typography key={`${i}${n}`} variant="caption" component="p" color="text.secondary">
          {n}
        </Typography>
      ))}
    </>
  );
}
