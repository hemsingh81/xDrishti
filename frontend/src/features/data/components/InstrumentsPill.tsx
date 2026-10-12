import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box } from '@mui/material';
import type { BarsOverview } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { instrumentGaps } from '../lib/freshness';
import { RibbonStat } from './RibbonStat';

/** Instruments with bars; "12 behind" (a warning, with its icon) and "3 without bars" say what is missing. */
export function InstrumentsPill({ bars }: { bars: BarsOverview }) {
  const { behind, empty } = instrumentGaps(bars);
  return (
    <RibbonStat
      label="Instruments"
      note={
        <>
          {behind && (
            <Box
              component="span"
              sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5, color: 'warning.main' }}
            >
              <WarningIcon fontSize="small" aria-hidden />
              <Box component="span" sx={{ fontWeight: 600 }}>
                {behind}
              </Box>
            </Box>
          )}
          {empty && <Box component="span">{empty}</Box>}
        </>
      }
    >
      {formatNumber(bars.instruments)}
    </RibbonStat>
  );
}
