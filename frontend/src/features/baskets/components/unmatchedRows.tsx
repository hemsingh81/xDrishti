import { Typography } from '@mui/material';
import type { BasketMember } from '@/shared/api';
import type { GridColumn } from '@/shared/ui/grid';
import { monoFontFamily } from '@/theme';

const prefix = 'unmatched:';

/** A row for a symbol of the source's list that is not in the instrument list: it has no instrument, only the symbol. */
export const unmatchedMember = (symbol: string): BasketMember => ({
  id: `${prefix}${symbol}`,
  position: 0,
  validFrom: '',
  validTo: null,
  note: null,
  addedBy: '',
  quantity: null,
  accountName: null,
  instrument: {
    id: `${prefix}${symbol}`,
    symbol,
    name: symbol,
    displayName: symbol,
    type: '',
    isin: null,
    series: null,
    sector: null,
    lotSize: 0,
    tickSize: null,
    expiryDate: null,
    fnoEligible: false,
    asmGsm: false,
    retired: false,
    tracked: {
      active: false,
      since: null,
      deactivatedAt: null,
      dataFrom: null,
      dataTo: null,
      lastUpdate: null,
      backfillRequestedAt: null,
      retiredButTracked: false,
    },
  },
});

export const isUnmatched = (member: BasketMember): boolean => member.id.startsWith(prefix);

/**
 * The columns with a different look for unmatched rows: the symbol greyed, every data column "no data", the grip and
 * the actions empty (there is nothing to copy). The other rows are drawn as before.
 */
export function withUnmatched(columns: readonly GridColumn<BasketMember>[]): GridColumn<BasketMember>[] {
  return columns.map((column) => ({
    ...column,
    render: (member) => {
      if (!isUnmatched(member)) return column.render ? column.render(member) : column.accessor?.(member);
      if (column.kind === 'handle') return null;
      return (
        <Typography
          component="span"
          color="text.disabled"
          sx={column.id === 'symbol' ? { fontFamily: monoFontFamily, fontWeight: 600 } : undefined}
        >
          {column.id === 'symbol' ? member.instrument.symbol : 'no data'}
        </Typography>
      );
    },
  }));
}
