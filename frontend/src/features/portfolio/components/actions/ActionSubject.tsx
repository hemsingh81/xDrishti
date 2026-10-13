import { Link, Typography } from '@mui/material';
import type { ActionItem } from '@/shared/api';
import { monoFontFamily } from '@/theme';
import { holdingIdOf, subjectOf } from '../../lib/actions';

const symbolSx = { fontFamily: monoFontFamily, fontWeight: 600, verticalAlign: 'baseline' } as const;

interface ActionSubjectProps {
  item: ActionItem;
  /** Names the subject for the row's accessible name. */
  id: string;
  onOpenHolding: () => void;
}

/** What the item is about: the holding's symbol as a link, or the account and sector with the sector's largest holding. */
export function ActionSubject({ item, id, onOpenHolding }: ActionSubjectProps) {
  if (item.symbol && holdingIdOf(item)) {
    return (
      <Link id={id} component="button" type="button" underline="always" onClick={onOpenHolding} sx={symbolSx}>
        {item.symbol}
      </Link>
    );
  }
  const largest = item.numbers.largestSymbol;
  return (
    <>
      <Typography id={id} component="span" sx={{ fontWeight: 600 }}>
        {subjectOf(item)}
      </Typography>
      {largest && item.numbers.largestHoldingId && (
        <Typography component="span" variant="body2" color="text.secondary">
          {'largest: '}
          <Link component="button" type="button" underline="always" onClick={onOpenHolding} sx={symbolSx}>
            {largest}
          </Link>
        </Typography>
      )}
    </>
  );
}
