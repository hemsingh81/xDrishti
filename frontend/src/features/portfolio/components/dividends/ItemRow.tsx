import { Box, Button, Checkbox, TableCell, TableRow, Typography } from '@mui/material';
import type { DividendPreviewItem } from '@/shared/api';
import { formatDate, formatInrPrecise, formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { warningText, type MatchDraft } from '../../lib/dividendImport';
import { MatchedTo } from './MatchedTo';

const number = { ...numeric, textAlign: 'right', whiteSpace: 'nowrap', verticalAlign: 'top' } as const;

export type ItemVariant = 'ready' | 'duplicates' | 'imported';

interface ItemRowProps {
  item: DividendPreviewItem;
  variant: ItemVariant;
  draft: MatchDraft | undefined;
  /** Possible duplicates: ticked to be imported anyway. */
  included: boolean;
  onInclude: (line: number, on: boolean) => void;
  /** Opens the picker for the row's name (any match of a row not yet imported may be changed). */
  onChange: (item: DividendPreviewItem) => void;
}

/** One row of the file as the preview understood it. Names are the file's text: plain text, wrapping anywhere. */
export function ItemRow({ item, variant, draft, included, onInclude, onChange }: ItemRowProps) {
  const changeable = variant !== 'imported';
  const remembered = item.match.kind === 'alias';
  return (
    <TableRow>
      {variant === 'duplicates' && (
        <TableCell padding="checkbox" sx={{ verticalAlign: 'top' }}>
          <Checkbox
            checked={included}
            onChange={(event) => {
              onInclude(item.line, event.target.checked);
            }}
            slotProps={{
              input: {
                'aria-label': `Include line ${String(item.line)}, ${item.name}, ${formatDate(item.payDate)}`,
              },
            }}
          />
        </TableCell>
      )}
      <TableCell sx={{ ...numeric, verticalAlign: 'top' }}>{item.line}</TableCell>
      <TableCell sx={{ whiteSpace: 'nowrap', verticalAlign: 'top' }}>
        <time dateTime={item.payDate}>{formatDate(item.payDate)}</time>
      </TableCell>
      <TableCell sx={{ minWidth: 140, overflowWrap: 'anywhere', verticalAlign: 'top' }}>{item.name}</TableCell>
      <TableCell sx={{ minWidth: 160, verticalAlign: 'top' }}>
        <MatchedTo item={item} draft={draft} />
        {changeable && (
          <Button
            size="small"
            aria-label={`Change the ${remembered ? 'remembered match' : 'match'} for ${item.name}`}
            onClick={() => {
              onChange(item);
            }}
          >
            Change
          </Button>
        )}
      </TableCell>
      <TableCell sx={number}>{item.perShare === null ? '—' : formatInrPrecise(item.perShare)}</TableCell>
      <TableCell sx={number}>{item.quantity === null ? '—' : formatNumber(item.quantity)}</TableCell>
      <TableCell sx={number}>{formatInrPrecise(item.amount)}</TableCell>
      <TableCell sx={{ minWidth: 160, verticalAlign: 'top' }}>
        {item.warnings.length === 0 ? (
          '—'
        ) : (
          <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
            {item.warnings.map((warning) => (
              <Typography component="li" variant="body2" key={warning}>
                {warningText(warning)}
              </Typography>
            ))}
          </Box>
        )}
      </TableCell>
    </TableRow>
  );
}
