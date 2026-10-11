import {
  Checkbox,
  Chip,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  TextField,
} from '@mui/material';
import { useState } from 'react';
import type { ImportPreviewRow } from '@/shared/api';
import { minAcquiredOn, todayIso } from '@/shared/lib/dates';
import { formatDate, formatInrPrecise, formatNumber } from '@/shared/lib/format';
import { monoFontFamily, numeric } from '@/theme';
import type { useImportDraft } from '../../hooks/useImportDraft';

type Draft = ReturnType<typeof useImportDraft>;

interface ImportTableProps {
  rows: readonly ImportPreviewRow[];
  draft: Draft;
  /** Messages from the API for a row's date, by row key. */
  serverErrors: Readonly<Record<string, string>>;
  disabled: boolean;
  onDateChange: (key: string, date: string) => void;
}

const number = { ...numeric, textAlign: 'right', whiteSpace: 'nowrap' } as const;

function RowView({
  row,
  draft,
  serverError,
  disabled,
  onDateChange,
}: Omit<ImportTableProps, 'rows' | 'serverErrors'> & { row: ImportPreviewRow; serverError: string | undefined }) {
  const already = row.alreadyImported;
  const message = already ? undefined : (draft.errors[row.key] ?? serverError);
  return (
    <TableRow hover={!already} sx={already ? { opacity: 0.75 } : undefined}>
      <TableCell padding="checkbox">
        <Checkbox
          checked={!already && draft.selected.has(row.key)}
          disabled={already || disabled}
          slotProps={{ input: { 'aria-label': `Select ${row.symbol} to apply a date` } }}
          onChange={(event) => {
            draft.toggle(row.key, event.target.checked);
          }}
        />
      </TableCell>
      <TableCell>
        <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', flexWrap: 'wrap', rowGap: 0.25 }}>
          <span style={{ fontFamily: monoFontFamily, fontWeight: 600 }}>{row.symbol}</span>
          {!row.mapped && <Chip size="small" variant="outlined" color="warning" label="Not in master list" />}
          {already && <Chip size="small" label="Already imported" />}
        </Stack>
        <div>{row.name}</div>
      </TableCell>
      <TableCell sx={number}>{formatNumber(row.qty)}</TableCell>
      <TableCell sx={number}>{formatInrPrecise(row.avgCost)}</TableCell>
      <TableCell sx={number}>{formatInrPrecise(row.lastPrice)}</TableCell>
      <TableCell sx={{ minWidth: 190 }}>
        {already ? (
          <span>Bought {formatDate(row.suggestedDate)}</span>
        ) : (
          <TextField
            size="small"
            type="date"
            value={draft.dateOf(row)}
            error={message !== undefined}
            helperText={message}
            disabled={disabled}
            onChange={(event) => {
              onDateChange(row.key, event.target.value);
            }}
            slotProps={{
              htmlInput: { 'aria-label': `Acquired on, ${row.symbol}`, min: minAcquiredOn, max: todayIso() },
            }}
          />
        )}
      </TableCell>
    </TableRow>
  );
}

/** A list longer than this is shown a page at a time, so thousands of date fields never render at once. */
export const pagedAbove = 200;
export const importPageSize = 100;

/** What Dhan holds: one row each with a date field; rows already in the system are marked and left out. */
export function ImportTable({ rows, draft, serverErrors, disabled, onDateChange }: ImportTableProps) {
  const some = draft.selectedCount > 0 && !draft.allSelected;
  const paged = rows.length > pagedAbove;
  const [page, setPage] = useState(0);
  const shown = paged ? rows.slice(page * importPageSize, (page + 1) * importPageSize) : rows;
  return (
    <>
      <TableContainer>
        <Table size="small" stickyHeader aria-label="Holdings at Dhan">
          <TableHead>
            <TableRow>
              <TableCell padding="checkbox">
                <Checkbox
                  checked={draft.allSelected}
                  indeterminate={some}
                  disabled={disabled || draft.importable.length === 0}
                  slotProps={{ input: { 'aria-label': 'Select all holdings to apply a date' } }}
                  onChange={(event) => {
                    draft.toggleAll(event.target.checked);
                  }}
                />
              </TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Holding</TableCell>
              <TableCell sx={{ ...number, fontWeight: 600 }}>Qty</TableCell>
              <TableCell sx={{ ...number, fontWeight: 600 }}>Avg cost</TableCell>
              <TableCell sx={{ ...number, fontWeight: 600 }}>LTP</TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Acquired on</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {shown.map((row) => (
              <RowView
                key={row.key}
                row={row}
                draft={draft}
                serverError={serverErrors[row.key]}
                disabled={disabled}
                onDateChange={onDateChange}
              />
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {paged && (
        <TablePagination
          component="div"
          count={rows.length}
          page={page}
          rowsPerPage={importPageSize}
          rowsPerPageOptions={[importPageSize]}
          onPageChange={(_event, next) => {
            setPage(next);
          }}
          labelDisplayedRows={({ from, to, count }) => `${String(from)}–${String(to)} of ${String(count)}`}
        />
      )}
    </>
  );
}
