import { Alert, Button, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { CsvFileRow, StartDataJobInput } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { csvFilesList, usePreviewCsvFiles } from '../api/csvQueries';
import { useCsvSelection } from '../hooks/useCsvSelection';
import {
  loadBlockers,
  maxPreviewFiles,
  isSelectable,
  needsForce,
  selectionBreakdown,
  selectionText,
  type CsvStateFilter,
} from '../lib/csvReview';
import { csvErrorText } from '../lib/errors';
import { CsvFileFilters } from './CsvFileFilters';
import { CsvFilesPanel } from './CsvFilesPanel';
import { CsvPreviewResults } from './CsvPreviewResults';
import { UndoCsvDialog } from './UndoCsvDialog';

interface CsvReviewDialogProps {
  /** A data job is running: Load and Undo wait for it. */
  busy: boolean;
  onStart: (body: StartDataJobInput) => void;
  onClose: () => void;
}

/**
 * Review the CSV folder before loading it: the files with their state, a preview (a dry run of the real reader and
 * row rules, nothing written) of the files checked, then "Load selected" for those, which starts a job the page follows.
 * Loaded files have Undo. A preview is optional; one that refuses a checked file stops the load.
 */
export function CsvReviewDialog({ busy, onStart, onClose }: CsvReviewDialogProps) {
  const [q, setQ] = useState('');
  const [state, setState] = useState<CsvStateFilter>('all');
  const [confirmingLoad, setConfirmingLoad] = useState(false);
  const [undoing, setUndoing] = useState<CsvFileRow | null>(null);
  const files = useInfiniteRows(csvFilesList({ q, state }));
  const selection = useCsvSelection();
  const preview = usePreviewCsvFiles();

  // "Select all loaded rows" acts on the files in the list now; the choice itself is kept by name across lists.
  const checkable = files.rows.filter(isSelectable);
  const chosenHere = checkable.filter((file) => selection.names.has(file.name)).length;
  const blockers = loadBlockers(selection.files, preview.data?.files ?? []);
  const canLoad = blockers.length === 0 && !busy;
  const force = needsForce(selection.files);
  const previewing = selection.files.slice(0, maxPreviewFiles);

  const load = () => {
    onStart({
      steps: ['csv', 'aggregate'],
      csvFiles: selection.files.map((file) => file.name),
      ...(force ? { forceCsv: true } : {}),
    });
    onClose();
  };

  return (
    <>
      <AppDialog open onClose={onClose} maxWidth="lg" fullScreenOnMobile>
        <AppDialogHeader subtitle="Check the files, preview what they would store, then load the ones you choose.">
          Review CSV files
        </AppDialogHeader>
        <AppDialogBody>
          <Stack spacing={2}>
            <CsvFileFilters
              q={q}
              onSearch={setQ}
              state={state}
              onState={setState}
              counts={files.lastPage?.counts}
              allChosen={checkable.length > 0 && chosenHere === checkable.length}
              someChosen={chosenHere > 0}
              disableAll={checkable.length === 0}
              onToggleAll={(on) => {
                selection.set(checkable, on);
              }}
            />
            <CsvFilesPanel
              result={files}
              filtered={q.trim() !== '' || state !== 'all'}
              listKey={`${q.trim()}|${state}`}
              selected={selection.names}
              full={selection.full}
              busy={busy}
              onToggle={selection.toggle}
              onUndo={setUndoing}
            />
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              {selectionText(selection.files.length)}
              {selection.full ? ' That is the most one load may take.' : ''}
            </Typography>
            {/* Always present, so a screen reader announces that the preview started and when it is ready. */}
            <Typography role="status" aria-label="Preview status" variant="body2" color="text.secondary">
              {preview.isPending
                ? `Previewing ${formatNumber(previewing.length)} ${previewing.length === 1 ? 'file' : 'files'}…`
                : preview.isSuccess
                  ? 'Preview ready.'
                  : ''}
            </Typography>
            {preview.isError && (
              <Alert severity="error">{csvErrorText(preview.error, 'The preview could not be made.')}</Alert>
            )}
            {preview.data && <CsvPreviewResults result={preview.data} />}
            {selection.files.length > 0 && blockers.length > 0 && (
              <Alert severity="warning" aria-label="Why the files cannot be loaded">
                <Typography variant="body2" sx={{ fontWeight: 600 }}>
                  Load selected is off:
                </Typography>
                <ul style={{ margin: 0, paddingLeft: 20 }}>
                  {blockers.map((reason) => (
                    <li key={reason}>{reason}</li>
                  ))}
                </ul>
              </Alert>
            )}
            {busy && <Alert severity="info">An update is running. Load and Undo wait until it ends.</Alert>}
          </Stack>
        </AppDialogBody>
        <AppDialogFooter>
          <Button onClick={onClose}>Close</Button>
          <Button
            variant="outlined"
            disabled={selection.files.length === 0}
            loading={preview.isPending}
            onClick={() => {
              preview.mutate(previewing.map((file) => file.name));
            }}
          >
            Preview
          </Button>
          <Button
            variant="contained"
            disabled={!canLoad}
            onClick={() => {
              setConfirmingLoad(true);
            }}
          >
            Load selected
          </Button>
        </AppDialogFooter>
      </AppDialog>
      <ConfirmDialog
        open={confirmingLoad}
        title={`Load ${formatNumber(selection.files.length)} ${selection.files.length === 1 ? 'file' : 'files'}?`}
        confirmLabel="Load"
        onConfirm={load}
        onCancel={() => {
          setConfirmingLoad(false);
        }}
      >
        {selectionBreakdown(selection.files)}. The bars are stored; bars from Dhan are never overwritten. Then the
        aggregates are rebuilt.
        {force ? ' Files that were loaded already are loaded again.' : ''}
      </ConfirmDialog>
      {undoing && (
        <UndoCsvDialog
          file={undoing}
          onClose={() => {
            setUndoing(null);
          }}
          onStarted={onClose}
        />
      )}
    </>
  );
}
