import { Typography } from '@mui/material';
import type { OfficialDailyCheck } from '@/shared/api';
import { checkTitles } from '../../lib/labels';
import { CheckSection } from './CheckSection';

/** The comparison with the broker's official daily candle: not available until those candles are fetched. */
export function OfficialDailySection({ check }: { check: OfficialDailyCheck }) {
  return (
    <CheckSection title={checkTitles.officialDaily} status={check.status}>
      <Typography>{check.message}</Typography>
    </CheckSection>
  );
}
