import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Button, Checkbox, Stack, Typography } from '@mui/material';
import type { CsvFileRow } from '@/shared/api';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { monoFontFamily } from '@/theme';
import { isLoadedFile, isSelectable, mappingHints, mappingProblems } from '../lib/csvReview';
import { useCsvList } from './csvListContext';

/** The instrument a file maps to, or the flag (a word with an icon) that says why it cannot be loaded. */
export function MappingCell({ file }: { file: CsvFileRow }) {
  if (isSelectable(file)) {
    return <Box sx={{ fontFamily: monoFontFamily }}>{file.instrument?.symbol ?? file.symbol}</Box>;
  }
  return (
    <Stack direction="row" sx={{ gap: 0.5, alignItems: 'center' }} title={mappingHints[file.mapping]}>
      <WarningIcon fontSize="small" color="warning" aria-hidden />
      <span>
        {mappingProblems[file.mapping] ?? file.mapping}
        {file.symbol ? ` (${file.symbol})` : ''}
      </span>
    </Stack>
  );
}

/** The file name, with a note under it when other loaded files of the instrument cover part of the same time. */
export function FileNameCell({ file }: { file: CsvFileRow }) {
  const names = file.overlapsWith ?? [];
  return (
    <Box sx={{ minWidth: 0 }}>
      <Box component="span" sx={{ fontFamily: monoFontFamily }}>
        {file.name}
      </Box>
      {names.length > 0 && (
        <Stack direction="row" sx={{ gap: 0.5, alignItems: 'center' }}>
          <WarningIcon fontSize="inherit" color="warning" aria-hidden />
          <Typography variant="caption" color="text.secondary">
            Also covers the same time: {names.join(', ')}
          </Typography>
        </Stack>
      )}
    </Box>
  );
}

/** What a loaded file stored: its rows (and that it differs now), the time range; when it was loaded is the tooltip. */
export function LoadedCell({ file }: { file: CsvFileRow }) {
  if (!isLoadedFile(file)) return <>—</>;
  return (
    <Stack title={file.loadedAt ? `Loaded ${formatIstDateTime(file.loadedAt)}` : undefined}>
      <span>
        {file.rows === null ? 'Rows not recorded' : `${formatNumber(file.rows)} rows`}
        {file.state === 'changed' ? ' · the file differs now' : ''}
      </span>
      {file.firstBar && file.lastBar && (
        <Typography variant="caption" color="text.secondary">
          {formatIstDateTime(file.firstBar)} to {formatIstDateTime(file.lastBar)}
        </Typography>
      )}
    </Stack>
  );
}

/** The checkbox of a file; a file that cannot be loaded, or one more than a load may name, cannot be checked. */
export function SelectCell({ file }: { file: CsvFileRow }) {
  const { selected, full, onToggle } = useCsvList();
  const on = selected.has(file.name);
  return (
    <Checkbox
      size="small"
      slotProps={{ input: { 'aria-label': `Select ${file.name}` } }}
      checked={on}
      disabled={!isSelectable(file) || (full && !on)}
      onChange={() => {
        onToggle(file);
      }}
    />
  );
}

/** Undo for a file that was loaded; it waits while a job runs. */
export function UndoCell({ file }: { file: CsvFileRow }) {
  const { busy, onUndo } = useCsvList();
  if (!isLoadedFile(file)) return null;
  return (
    <Button
      size="small"
      color="inherit"
      disabled={busy}
      aria-label={`Undo load of ${file.name}`}
      onClick={() => {
        onUndo(file);
      }}
    >
      Undo
    </Button>
  );
}
