import EditIcon from '@mui/icons-material/EditOutlined';
import { Chip, IconButton, Stack, TableCell, TableRow, Typography } from '@mui/material';
import { useState } from 'react';
import type { LotRow } from '@/shared/api';
import { formatDate, formatInr, formatInrPrecise, formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { Pnl, Xirr } from './Figures';
import { LotDateEditor } from './LotDateEditor';

const sourceLabels: Record<string, string> = { import: 'Imported', added: 'Added by sync', manual: 'Date edited' };

function Acquired({ holdingId, lot }: { holdingId: string; lot: LotRow }) {
  const [editing, setEditing] = useState(false);
  const name = formatDate(lot.acquiredOn);
  if (editing) {
    return (
      <LotDateEditor
        holdingId={holdingId}
        lot={lot}
        lotName={name}
        onDone={() => {
          setEditing(false);
        }}
      />
    );
  }
  return (
    <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.25 }}>
      <time dateTime={lot.acquiredOn}>{name}</time>
      {lot.usesDefaultDate && <Chip size="small" variant="outlined" color="warning" label="Default date" />}
      <IconButton
        size="small"
        aria-label={`Edit the date of the lot bought on ${name}`}
        onClick={() => {
          setEditing(true);
        }}
      >
        <EditIcon fontSize="small" />
      </IconButton>
      <Typography component="span" variant="caption" color="text.secondary">
        {sourceLabels[lot.source] ?? lot.source}
      </Typography>
    </Stack>
  );
}

function Status({ lot }: { lot: LotRow }) {
  if (lot.status !== 'closed') return <>Open</>;
  const sold = lot.closedOn ? `Closed ${formatDate(lot.closedOn)}` : 'Closed';
  const price = lot.closePrice === null ? '' : ` at ${formatInrPrecise(lot.closePrice)}`;
  return (
    <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.25 }}>
      <span>{`${sold}${price}`}</span>
      {lot.estimated && (
        <Chip size="small" variant="outlined" label="Estimated" title="Sold at the last price a sync saw" />
      )}
    </Stack>
  );
}

const number = { ...numeric, textAlign: 'right', whiteSpace: 'nowrap' } as const;

/** One lot: when it was bought (editable), how much, what it cost and how it did. */
export function LotRowView({ holdingId, lot }: { holdingId: string; lot: LotRow }) {
  const { returns } = lot;
  return (
    <TableRow>
      <TableCell sx={{ minWidth: 200 }}>
        <Acquired holdingId={holdingId} lot={lot} />
      </TableCell>
      <TableCell sx={number}>{formatNumber(lot.qty)}</TableCell>
      <TableCell sx={number}>{formatInrPrecise(lot.unitCost)}</TableCell>
      <TableCell sx={number}>{formatInr(returns.invested)}</TableCell>
      <TableCell sx={number}>
        {returns.value === null ? (
          <Chip size="small" variant="outlined" color="warning" label="No price yet" />
        ) : (
          formatInr(returns.value)
        )}
      </TableCell>
      <TableCell sx={number}>
        <Pnl value={returns.pnl} />
      </TableCell>
      <TableCell sx={number}>
        <Xirr returns={returns} />
      </TableCell>
      <TableCell>
        <Status lot={lot} />
      </TableCell>
    </TableRow>
  );
}
