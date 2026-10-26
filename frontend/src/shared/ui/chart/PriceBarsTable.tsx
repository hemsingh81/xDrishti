import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { formatPrice } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { dateClock } from './priceChartModel';
import type { PriceBar } from './priceChartTypes';

/** Rows shown: the table is the alternative to the picture, not a data export. */
export const barsTableCap = 500;

/** The bars as a table (first 500 rows, said), times in India time. */
export function PriceBarsTable({ label, bars, id }: { label: string; bars: readonly PriceBar[]; id: string }) {
  const rows = bars.slice(0, barsTableCap);
  return (
    <div id={id}>
      <TableContainer sx={{ border: 1, borderColor: 'divider', borderRadius: 1, maxHeight: 360 }}>
        <Table size="small" stickyHeader aria-label={`${label}, bars`}>
          <TableHead>
            <TableRow>
              <TableCell>Time (IST)</TableCell>
              {['Open', 'High', 'Low', 'Close', 'Volume'].map((h) => (
                <TableCell key={h} align="right">
                  {h}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((b) => (
              <TableRow key={b.t}>
                <TableCell component="th" scope="row" sx={{ whiteSpace: 'nowrap' }}>
                  {dateClock(b.t)}
                </TableCell>
                <TableCell align="right" sx={numeric}>
                  {formatPrice(b.o)}
                </TableCell>
                <TableCell align="right" sx={numeric}>
                  {formatPrice(b.h)}
                </TableCell>
                <TableCell align="right" sx={numeric}>
                  {formatPrice(b.l)}
                </TableCell>
                <TableCell align="right" sx={numeric}>
                  {formatPrice(b.c)}
                </TableCell>
                <TableCell align="right" sx={numeric}>
                  {b.v.toLocaleString('en-IN')}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <Typography variant="caption" color="text.secondary" role="status">
        {bars.length > barsTableCap
          ? `The first ${String(barsTableCap)} of ${String(bars.length)} bars are listed.`
          : `All ${String(bars.length)} bars are listed.`}
      </Typography>
    </div>
  );
}
