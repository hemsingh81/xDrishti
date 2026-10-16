import { Chip, TableCell, TableRow, Typography } from '@mui/material';
import type { LedgerEntry } from '@/shared/api';
import { formatDate, formatInrPrecise } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { amountDirection, classLabel, signedPrecise } from '../../lib/ledger';
import { Trend } from '../Figures';

const number = { ...numeric, textAlign: 'right', whiteSpace: 'nowrap' } as const;

/**
 * One ledger entry. The description and the narration are Dhan's free text: they are shown as plain text (never as markup)
 * and wrap anywhere, so a long word cannot widen the table. The amount carries its sign and an arrow as well as the colour.
 * An entry that came from a downloaded report (the depository-charges sheet) is marked Imported: it can be undone there.
 */
export function LedgerRow({ entry }: { entry: LedgerEntry }) {
  return (
    <TableRow>
      <TableCell sx={{ whiteSpace: 'nowrap', verticalAlign: 'top' }}>
        <time dateTime={entry.date}>{formatDate(entry.date)}</time>
      </TableCell>
      <TableCell sx={{ minWidth: 220, overflowWrap: 'anywhere' }}>
        <Typography component="span" sx={{ display: 'block' }}>
          {entry.description}
        </Typography>
        {entry.narration && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            {entry.narration}
          </Typography>
        )}
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          {`Voucher ${entry.voucherNo}${entry.exchange ? ` · ${entry.exchange}` : ''}`}
        </Typography>
      </TableCell>
      <TableCell sx={{ verticalAlign: 'top' }}>
        <Chip size="small" variant="outlined" label={classLabel(entry.class)} />
        {entry.source === 'import' && (
          <Chip size="small" variant="outlined" color="primary" label="Imported" sx={{ ml: 0.5 }} />
        )}
      </TableCell>
      <TableCell sx={{ ...number, verticalAlign: 'top' }}>
        <Trend direction={amountDirection(entry.amount)}>{signedPrecise(entry.amount)}</Trend>
      </TableCell>
      <TableCell sx={{ ...number, verticalAlign: 'top' }}>
        {entry.runningBalance === null ? '—' : formatInrPrecise(entry.runningBalance)}
      </TableCell>
    </TableRow>
  );
}
