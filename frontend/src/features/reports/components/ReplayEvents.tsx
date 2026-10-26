import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { formatIstDayMonth, formatIstTime, formatPrice } from '@/shared/lib/format';
import { numeric } from '@/theme';
import type { ReplayEvent } from '../lib/replayModel';

/** The events of the trade as a table: the same numbers as the markers on the chart, so the picture is never the only carrier. */
export function ReplayEvents({ events }: { events: readonly ReplayEvent[] }) {
  return (
    <>
      <TableContainer
        tabIndex={0}
        role="region"
        aria-label="Events of the trade, scrolls sideways"
        sx={{ border: 1, borderColor: 'divider', borderRadius: 1 }}
      >
        <Table size="small" aria-label="Events of the trade">
          <TableHead>
            <TableRow>
              <TableCell>Event</TableCell>
              <TableCell>Time (IST)</TableCell>
              <TableCell align="right">Price</TableCell>
              <TableCell align="right">Share</TableCell>
              <TableCell>Reason</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {events.map((e) => (
              <TableRow key={e.id}>
                <TableCell component="th" scope="row">
                  {e.kind === 'entry' ? 'Entry' : e.kind === 'add' ? 'Add' : 'Exit'}
                </TableCell>
                <TableCell
                  sx={{ whiteSpace: 'nowrap' }}
                >{`${formatIstDayMonth(e.t * 1000)} ${formatIstTime(e.t * 1000)}`}</TableCell>
                <TableCell align="right" sx={numeric}>
                  {formatPrice(e.price)}
                </TableCell>
                <TableCell align="right" sx={numeric}>
                  {`${String(Math.round(e.share * 1000) / 10)}%`}
                </TableCell>
                <TableCell>{[e.reason.replaceAll('_', ' '), e.note].filter(Boolean).join(': ') || '—'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
        A trailing exit's level path is not stored, so none is drawn.
      </Typography>
    </>
  );
}
