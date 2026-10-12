import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import type { BarRow } from '@/shared/api';
import { formatIstDate, formatIstTimeAndDay, formatNumber, formatPrice } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { sourceLabel } from '../lib/labels';
import { isDayTimeframe } from '../lib/timeframes';

interface BarsTableProps {
  timeframe: string;
  bars: readonly BarRow[];
}

const timeOf = (timeframe: string, ts: string) =>
  isDayTimeframe(timeframe) ? formatIstDate(ts) : formatIstTimeAndDay(ts);

/**
 * The newest bars, newest first. A derived bar says how many 1-minute bars it was built from and whether it is
 * complete (in words); a stored 1-minute bar says where it came from instead.
 */
export function BarsTable({ timeframe, bars }: BarsTableProps) {
  const stored = timeframe === '1m';
  const number = { textAlign: 'right', ...numeric } as const;
  return (
    <TableContainer
      tabIndex={0}
      role="region"
      aria-label="Latest bars"
      sx={{ maxHeight: { xs: 'none', sm: '65vh' }, border: 1, borderColor: 'divider', borderRadius: 1 }}
    >
      <Table size="small" stickyHeader aria-label={`Latest ${timeframe} bars`}>
        <TableHead>
          <TableRow>
            <TableCell>Time (IST)</TableCell>
            <TableCell sx={number}>Open</TableCell>
            <TableCell sx={number}>High</TableCell>
            <TableCell sx={number}>Low</TableCell>
            <TableCell sx={number}>Close</TableCell>
            <TableCell sx={number}>Volume</TableCell>
            {stored ? (
              <TableCell>Source</TableCell>
            ) : (
              <>
                <TableCell sx={number}>1m bars</TableCell>
                <TableCell>Complete</TableCell>
              </>
            )}
          </TableRow>
        </TableHead>
        <TableBody>
          {bars.map((bar) => (
            <TableRow key={bar.ts}>
              <TableCell component="th" scope="row" sx={{ whiteSpace: 'nowrap', fontWeight: 400 }}>
                <Box component="time" dateTime={bar.ts}>
                  {timeOf(timeframe, bar.ts)}
                </Box>
              </TableCell>
              <TableCell sx={number}>{formatPrice(bar.open)}</TableCell>
              <TableCell sx={number}>{formatPrice(bar.high)}</TableCell>
              <TableCell sx={number}>{formatPrice(bar.low)}</TableCell>
              <TableCell sx={number}>{formatPrice(bar.close)}</TableCell>
              <TableCell sx={number}>{formatNumber(bar.volume)}</TableCell>
              {stored ? (
                <TableCell>{sourceLabel(bar.source)}</TableCell>
              ) : (
                <>
                  <TableCell sx={number}>{formatNumber(bar.bars)}</TableCell>
                  <TableCell>{bar.complete ? 'Complete' : 'Partial'}</TableCell>
                </>
              )}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
