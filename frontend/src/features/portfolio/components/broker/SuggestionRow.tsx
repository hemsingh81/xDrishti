import { Button, Stack, TableCell, TableRow, Typography } from '@mui/material';
import type { LedgerEntry } from '@/shared/api';
import { formatDate, formatInrPrecise } from '@/shared/lib/format';
import { numeric } from '@/theme';

const number = { ...numeric, textAlign: 'right', whiteSpace: 'nowrap', verticalAlign: 'top' } as const;

interface SuggestionRowProps {
  entry: LedgerEntry;
  onAccept: (entry: LedgerEntry) => void;
  onDismiss: (entry: LedgerEntry) => void;
}

/** One suggested dividend: the ledger entry (Dhan's text shown as plain text) and the two decisions, both final. */
export function SuggestionRow({ entry, onAccept, onDismiss }: SuggestionRowProps) {
  const name = `${formatInrPrecise(entry.amount)} of ${formatDate(entry.date)}`;
  return (
    <TableRow>
      <TableCell sx={{ whiteSpace: 'nowrap', verticalAlign: 'top' }}>
        <time dateTime={entry.date}>{formatDate(entry.date)}</time>
      </TableCell>
      <TableCell sx={{ minWidth: 200, overflowWrap: 'anywhere', verticalAlign: 'top' }}>
        <Typography component="span" sx={{ display: 'block' }}>
          {entry.description}
        </Typography>
        {entry.narration && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            {entry.narration}
          </Typography>
        )}
      </TableCell>
      <TableCell sx={number}>{formatInrPrecise(entry.amount)}</TableCell>
      <TableCell sx={{ textAlign: 'right', verticalAlign: 'top' }}>
        <Stack direction="row" sx={{ gap: 0.5, justifyContent: 'flex-end', flexWrap: 'wrap' }}>
          <Button
            size="small"
            variant="outlined"
            aria-label={`Accept the suggested dividend of ${name}`}
            onClick={() => {
              onAccept(entry);
            }}
          >
            Accept
          </Button>
          <Button
            size="small"
            color="inherit"
            aria-label={`Dismiss the suggested dividend of ${name}`}
            onClick={() => {
              onDismiss(entry);
            }}
          >
            Dismiss
          </Button>
        </Stack>
      </TableCell>
    </TableRow>
  );
}
