import { Button, Chip, Stack, TableCell, TableRow, Typography } from '@mui/material';
import { useEffect, useRef, useState } from 'react';
import type { Trade } from '@/shared/api';
import { formatDate, formatInrPrecise, formatIstDate, formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { tradeName } from '../../lib/tradeText';

const number = { ...numeric, textAlign: 'right', whiteSpace: 'nowrap' } as const;
const sources: Record<string, string> = { csv: 'CSV import', manual: 'By hand' };

/** The time of day of a trade without the seconds ("09:15"). */
const timeOf = (time: string): string => time.slice(0, 5);

interface TradeRowProps {
  trade: Trade;
  onVoid: (trade: Trade) => void;
}

/**
 * One trade. A voided trade is struck through and says by whom and when; it has no Void button. The side is a word, not only
 * a colour.
 */
export function TradeRow({ trade, onVoid }: TradeRowProps) {
  // A void is final on this screen, so it asks first: focus moves to the safe answer, and back to Void on "No".
  const [asking, setAsking] = useState(false);
  const noRef = useRef<HTMLButtonElement>(null);
  const voidRef = useRef<HTMLButtonElement>(null);
  const returning = useRef(false);
  useEffect(() => {
    if (asking) noRef.current?.focus();
    else if (returning.current) {
      returning.current = false;
      voidRef.current?.focus();
    }
  }, [asking]);
  const struck = trade.voided ? { textDecoration: 'line-through' } : {};
  const sell = trade.side === 'sell';
  return (
    <TableRow>
      <TableCell sx={{ minWidth: 150 }}>
        <span style={struck}>
          <time dateTime={trade.tradeDate}>{formatDate(trade.tradeDate)}</time>
          {trade.tradeTime ? ` ${timeOf(trade.tradeTime)}` : ''}
        </span>
        {trade.voided && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            {`Voided${trade.voidedBy ? ` by ${trade.voidedBy}` : ''}${trade.voidedAt ? ` on ${formatIstDate(trade.voidedAt)}` : ''}`}
          </Typography>
        )}
      </TableCell>
      <TableCell>
        <Chip size="small" variant="outlined" color={sell ? 'warning' : 'success'} label={sell ? 'Sell' : 'Buy'} />
      </TableCell>
      <TableCell sx={{ ...number, ...struck }}>{formatNumber(trade.qty)}</TableCell>
      <TableCell sx={{ ...number, ...struck }}>{formatInrPrecise(trade.price)}</TableCell>
      <TableCell sx={{ ...number, ...struck }}>{formatInrPrecise(trade.charges)}</TableCell>
      <TableCell>{sources[trade.source] ?? trade.source}</TableCell>
      <TableCell sx={{ textAlign: 'right' }}>
        {!trade.voided &&
          (asking ? (
            <Stack
              role="group"
              aria-label={`Void the ${tradeName(trade)}?`}
              direction="row"
              sx={{ gap: 0.5, alignItems: 'center', justifyContent: 'flex-end', flexWrap: 'wrap' }}
            >
              <Typography variant="caption" component="span">
                Void this trade?
              </Typography>
              <Button
                size="small"
                color="error"
                variant="contained"
                aria-label={`Yes, void the ${tradeName(trade)}`}
                onClick={() => {
                  setAsking(false);
                  onVoid(trade);
                }}
              >
                Yes
              </Button>
              <Button
                size="small"
                ref={noRef}
                aria-label={`No, keep the ${tradeName(trade)}`}
                onClick={() => {
                  setAsking(false);
                  returning.current = true;
                }}
              >
                No
              </Button>
            </Stack>
          ) : (
            <Button
              size="small"
              color="error"
              ref={voidRef}
              aria-label={`Void the ${tradeName(trade)}`}
              onClick={() => {
                setAsking(true);
              }}
            >
              Void
            </Button>
          ))}
      </TableCell>
    </TableRow>
  );
}
