import ContentPasteIcon from '@mui/icons-material/ContentPasteOutlined';
import { Button, Stack, Typography } from '@mui/material';
import { formatNumber } from '@/shared/lib/format';
import { stateAlpha } from '@/theme';

interface BulkBarProps {
  selected: number;
  /** Rows on this page / matching results, to offer "select all N results". */
  pageRows: number;
  allOnPageSelected: boolean;
  total: number;
  /** Reading the ids of all results. */
  selectingAll: boolean;
  /** A track / untrack call is running. */
  busy: boolean;
  onSelectAll: () => void;
  onClear: () => void;
  onTrack: () => void;
  onUntrack: () => void;
  onPaste: () => void;
  /** Add the selection to baskets. */
  onAddToBasket: () => void;
}

const lockedOpacity = { '&[aria-disabled="true"]': { opacity: stateAlpha.busy, cursor: 'default' } } as const;

/** The selection message with its "select all" / "clear" link, announced politely to screen readers. */
function SelectionMessage({
  selected,
  pageRows,
  allOnPageSelected,
  total,
  selectingAll,
  onSelectAll,
  onClear,
}: Pick<
  BulkBarProps,
  'selected' | 'pageRows' | 'allOnPageSelected' | 'total' | 'selectingAll' | 'onSelectAll' | 'onClear'
>) {
  if (selected === 0) {
    return (
      <Typography role="status" color="text.secondary">
        Select instruments to track or untrack them in bulk
      </Typography>
    );
  }
  const everything = selected >= total;
  return (
    <Stack direction="row" sx={{ flexWrap: 'wrap', columnGap: 1, alignItems: 'center' }}>
      <Typography role="status" sx={{ fontWeight: 600 }}>
        {everything && total > pageRows
          ? `All ${formatNumber(total)} results are selected`
          : `${formatNumber(selected)} selected`}
      </Typography>
      {allOnPageSelected && !everything && total > pageRows && (
        <Button size="small" loading={selectingAll} onClick={onSelectAll}>
          {`Select all ${formatNumber(total)} results`}
        </Button>
      )}
      <Button size="small" onClick={onClear}>
        Clear selection
      </Button>
    </Stack>
  );
}

/**
 * The actions over the selection: Track, Untrack, Add to basket and Paste symbols, which needs no selection. While a call runs the buttons are `aria-disabled`, so one that has focus keeps it.
 */
export function BulkBar(props: BulkBarProps) {
  const { selected, busy, onTrack, onUntrack, onPaste, onAddToBasket } = props;
  const count = selected > 0 ? ` (${formatNumber(selected)})` : '';
  const idle = selected === 0;
  const guard = (action: () => void) => () => {
    if (!busy) action();
  };
  return (
    <Stack
      direction="row"
      sx={{ flexWrap: 'wrap', gap: 1.5, alignItems: 'center', justifyContent: 'space-between', px: 2, py: 1 }}
    >
      <SelectionMessage {...props} />
      <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1 }}>
        <Button
          size="small"
          variant="contained"
          disabled={idle}
          aria-disabled={busy || undefined}
          sx={lockedOpacity}
          onClick={guard(onTrack)}
        >
          {`Track${count}`}
        </Button>
        <Button
          size="small"
          variant="outlined"
          disabled={idle}
          aria-disabled={busy || undefined}
          sx={lockedOpacity}
          onClick={guard(onUntrack)}
        >
          {`Untrack${count}`}
        </Button>
        <Button size="small" variant="outlined" disabled={idle} onClick={onAddToBasket}>
          {`Add to basket${count}`}
        </Button>
        <Button size="small" startIcon={<ContentPasteIcon />} onClick={onPaste}>
          Paste symbols
        </Button>
      </Stack>
    </Stack>
  );
}
