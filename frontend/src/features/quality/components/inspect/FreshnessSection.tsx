import { Typography } from '@mui/material';
import type { FreshnessCheck } from '@/shared/api';
import { formatDate, formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { checkTitles } from '../../lib/labels';
import { CheckSection } from './CheckSection';

/** Freshness: the last bar against the last closed session of the trading calendar, counted in trading days. */
export function FreshnessSection({ check }: { check: FreshnessCheck }) {
  return (
    <CheckSection title={checkTitles.freshness} status={check.status}>
      <Typography>
        {check.lastBar ? (
          <>
            The last bar is <time dateTime={check.lastBar}>{formatIstDateTime(check.lastBar)}</time>; the last closed
            session is <time dateTime={check.expectedSession}>{formatDate(check.expectedSession)}</time>.{' '}
            {check.daysBehind > 0
              ? `Behind by ${formatNumber(check.daysBehind)} trading ${check.daysBehind === 1 ? 'day' : 'days'}.`
              : 'Up to date.'}
          </>
        ) : (
          'There are no bars yet.'
        )}
      </Typography>
    </CheckSection>
  );
}
