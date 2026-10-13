import { Box, TableCell, TableRow, Typography } from '@mui/material';
import type { TradeSecurity } from '@/shared/api';
import { formatDate, formatNumber } from '@/shared/lib/format';
import { monoFontFamily, numeric } from '@/theme';
import { countOf, reasonText, statusOf } from '../../lib/tradeText';
import { Pnl } from '../Figures';
import { Change } from './Change';
import { CostNote } from './CostNote';
import { RebuildStatusChip } from './RebuildStatusChip';
import { stickyFirst } from './stickyFirst';

const number = { ...numeric, textAlign: 'right', whiteSpace: 'nowrap' } as const;

/** One security of the file: its trades, dates and what rebuilding its holding's lots would do, with the status as icon and word. */
export function SecurityRow({ security }: { security: TradeSecurity }) {
  const { rebuild } = security;
  const status = statusOf(rebuild);
  const held = status !== 'no_holding';
  const reason = reasonText(rebuild);
  return (
    <TableRow>
      <TableCell component="th" scope="row" sx={stickyFirst}>
        <Typography component="span" sx={{ fontFamily: monoFontFamily, fontWeight: 600, overflowWrap: 'anywhere' }}>
          {security.symbol}
        </Typography>
        {security.isin && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            {security.isin}
          </Typography>
        )}
      </TableCell>
      <TableCell sx={{ minWidth: 200 }}>
        <RebuildStatusChip rebuild={rebuild} />
        {reason && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
            {reason}
          </Typography>
        )}
        <CostNote rebuild={rebuild} sells={security.sells} />
        {rebuild.adjustedFor > 0 && (
          <Box component="span" sx={{ display: 'block', mt: 0.5 }}>
            <Typography variant="caption" color="text.secondary">
              {`Adjusted for ${countOf(rebuild.adjustedFor, 'corporate action')}`}
            </Typography>
          </Box>
        )}
      </TableCell>
      <TableCell sx={{ minWidth: 150 }}>
        <Typography component="span" className="numeric" sx={{ display: 'block' }}>
          {`${formatNumber(security.buys)} buy · ${formatNumber(security.sells)} sell`}
        </Typography>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          <time dateTime={security.firstTradeDate}>{formatDate(security.firstTradeDate)}</time>
          {' – '}
          <time dateTime={security.lastTradeDate}>{formatDate(security.lastTradeDate)}</time>
        </Typography>
      </TableCell>
      <TableCell sx={number}>{held ? <Change from={rebuild.lotsBefore} to={rebuild.lotsAfter} /> : '—'}</TableCell>
      <TableCell sx={number}>
        {held ? <Change from={rebuild.openingQty.before} to={rebuild.openingQty.after} /> : '—'}
      </TableCell>
      <TableCell sx={number}>
        {held && rebuild.realisedPnl !== null ? <Pnl value={rebuild.realisedPnl} /> : '—'}
      </TableCell>
    </TableRow>
  );
}
