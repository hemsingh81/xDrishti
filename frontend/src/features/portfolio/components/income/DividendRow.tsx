import { Button, Chip, TableCell, TableRow, Typography } from '@mui/material';
import type { DividendView } from '@/shared/api';
import { formatDate, formatInrPrecise, formatIstDate } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { dividendName } from '../../lib/income';

const number = { ...numeric, textAlign: 'right', whiteSpace: 'nowrap' } as const;

interface DividendRowProps {
  dividend: DividendView;
  /** Also show which holding the dividend is for (the account's list). */
  showHolding: boolean;
  onVoid: (dividend: DividendView) => void;
}

/**
 * One dividend. A voided dividend is struck through, says by whom and when, and has no Void button. The note is the owner's
 * free text: plain text, wrapping anywhere so a long word cannot widen the table.
 */
export function DividendRow({ dividend, showHolding, onVoid }: DividendRowProps) {
  const struck = dividend.voided ? { textDecoration: 'line-through' } : {};
  return (
    <TableRow>
      <TableCell sx={{ minWidth: 120, verticalAlign: 'top' }}>
        <span style={struck}>
          <time dateTime={dividend.payDate}>{formatDate(dividend.payDate)}</time>
        </span>
        {dividend.voided && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            {`Voided${dividend.voidedBy ? ` by ${dividend.voidedBy}` : ''}${dividend.voidedAt ? ` on ${formatIstDate(dividend.voidedAt)}` : ''}`}
          </Typography>
        )}
      </TableCell>
      {showHolding && (
        <TableCell sx={{ verticalAlign: 'top', overflowWrap: 'anywhere' }}>
          {dividend.symbol ?? <Chip size="small" variant="outlined" label="Unallocated" />}
        </TableCell>
      )}
      <TableCell sx={{ ...number, ...struck, verticalAlign: 'top' }}>{formatInrPrecise(dividend.amount)}</TableCell>
      <TableCell sx={{ ...number, ...struck, verticalAlign: 'top' }}>
        {dividend.perShare === null ? '—' : formatInrPrecise(dividend.perShare)}
      </TableCell>
      <TableCell sx={{ minWidth: 140, overflowWrap: 'anywhere', verticalAlign: 'top' }}>
        {dividend.note ?? '—'}
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          {`Entered by ${dividend.createdBy} on ${formatIstDate(dividend.createdAt)}`}
        </Typography>
      </TableCell>
      <TableCell sx={{ textAlign: 'right', verticalAlign: 'top' }}>
        {dividend.voided ? (
          <Chip size="small" variant="outlined" label="Voided" />
        ) : (
          <Button
            size="small"
            color="error"
            aria-label={`Void the ${dividendName(dividend)}`}
            onClick={() => {
              onVoid(dividend);
            }}
          >
            Void
          </Button>
        )}
      </TableCell>
    </TableRow>
  );
}
