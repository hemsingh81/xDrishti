import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import type { ActionsImportResult } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useImportActions } from '../api/actionMutations';
import { maxImportLines } from '../lib/actions';
import { actionErrorText, fieldErrors } from '../lib/errors';
import { ImportAdjustWarnings } from './ImportAdjustWarnings';
import { ImportPreviewResult } from './ImportPreviewResult';

const example = 'symbol,exDate,kind,numerator,denominator,note\nINFY,2026-09-01,split,1,5,Stock split';

/** The summary toast of an import: what was written and what was left out. */
function importedText(imported: number, duplicates: number, invalid: number): string {
  const left = [
    duplicates > 0 ? `${formatNumber(duplicates)} duplicates skipped` : null,
    invalid > 0 ? `${formatNumber(invalid)} invalid lines skipped` : null,
  ].filter((part): part is string => part !== null);
  return `Imported ${formatNumber(imported)} ${imported === 1 ? 'action' : 'actions'}${left.length > 0 ? ` (${left.join(', ')})` : ''}.`;
}

/**
 * Import splits and bonus issues from a CSV file or pasted text. "Preview" reads the text and counts what would be
 * imported, which symbols match no instrument and which lines are wrong, without saving anything; "Import" then saves
 * them (they are applied at once). Lines inside CSV history that is adjusted already are listed in the preview and need
 * the owner's explicit consent (a checkbox) before Import is enabled; Import then sends the consent with the request.
 */
export function ImportActionsDialog({ onClose }: { onClose: () => void }) {
  const { notify } = useToast();
  const run = useImportActions();
  const fileInput = useRef<HTMLInputElement>(null);
  const textRef = useRef<HTMLTextAreaElement>(null);
  const [text, setText] = useState('');
  const [fileName, setFileName] = useState<string | null>(null);
  const [fileProblem, setFileProblem] = useState<string | null>(null);
  const [consent, setConsent] = useState(false);
  const [previewed, setPreviewed] = useState<{ text: string; result: ActionsImportResult } | null>(null);
  // A preview counts for the text it read; once the text changes it has to be run again.
  const shown = previewed?.text === text ? previewed.result : null;
  const messages = Object.values(fieldErrors(run.error));
  const twice = shown?.adjustsCsvTwice ?? 0;

  const change = (next: string, name: string | null) => {
    setText(next);
    setFileName(name);
    setFileProblem(null);
    setPreviewed(null);
    setConsent(false);
    run.reset();
  };

  const readFile = async (file: File) => {
    try {
      change(await file.text(), file.name);
    } catch {
      setFileProblem('The file could not be read.');
    }
  };

  const preview = async () => {
    try {
      setConsent(false);
      setPreviewed({ text, result: await run.mutateAsync({ text, preview: true }) });
    } catch {
      // The alert shows what the server said.
    }
  };

  const save = async () => {
    try {
      const done = await run.mutateAsync({ text, preview: false, acknowledge: twice > 0 && consent });
      notify(importedText(done.imported, done.duplicates, done.invalid));
      onClose();
    } catch {
      // The alert shows what the server said.
    }
  };

  return (
    <AppDialog open onClose={onClose} busy={run.isPending} maxWidth="md" initialFocusRef={textRef}>
      <AppDialogHeader>Import corporate actions from CSV</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <Typography variant="body2" color="text.secondary">
            Columns: symbol, exDate, kind (split or bonus), numerator, denominator and an optional note, with a header
            line, for example:
          </Typography>
          <Typography component="pre" variant="body2" sx={{ m: 0, overflowX: 'auto', fontFamily: 'monospace' }}>
            {example}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Up to {formatNumber(maxImportLines)} lines. Split 1:5 is old shares : new shares (prices before the ex-date
            × 0.2); bonus 1:1 is bonus shares : shares held (× 0.5). Symbols that match no instrument are listed, never
            guessed; repeats of an existing action are skipped. Imported actions are applied at once. Lines whose
            ex-date is on or before the day an instrument’s CSV files end are listed in the preview: those prices are
            already adjusted, and importing them needs your consent.
          </Typography>
          <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
            <input
              ref={fileInput}
              type="file"
              hidden
              aria-label="CSV file"
              accept=".csv,.txt,text/csv,text/plain"
              onChange={(event) => {
                const file = event.target.files?.[0];
                if (file) void readFile(file);
                event.target.value = '';
              }}
            />
            <Button
              variant="outlined"
              onClick={() => {
                fileInput.current?.click();
              }}
            >
              Choose file
            </Button>
            <Typography variant="body2" color="text.secondary">
              {fileName ?? 'or paste the text below'}
            </Typography>
          </Stack>
          {fileProblem && <Alert severity="error">{fileProblem}</Alert>}
          <TextField
            multiline
            minRows={5}
            maxRows={12}
            fullWidth
            label="CSV text"
            value={text}
            inputRef={textRef}
            onChange={(event) => {
              change(event.target.value, null);
            }}
            slotProps={{ htmlInput: { spellCheck: false, style: { fontFamily: 'monospace' } } }}
          />
          {run.isError && (
            <Alert severity="error">
              {actionErrorText(run.error, 'The import could not be read.')} {messages.join(' ')}
            </Alert>
          )}
          {shown && <ImportPreviewResult result={shown} />}
          {shown && <ImportAdjustWarnings result={shown} consent={consent} onConsentChange={setConsent} />}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={run.isPending}>
          Cancel
        </Button>
        <Button
          variant="outlined"
          disabled={text.trim() === '' || run.isPending}
          loading={run.isPending && run.variables.preview}
          onClick={() => {
            void preview();
          }}
        >
          Preview
        </Button>
        <Button
          variant="contained"
          disabled={!shown || shown.importable === 0 || run.isPending || (twice > 0 && !consent)}
          loading={run.isPending && !run.variables.preview}
          onClick={() => {
            void save();
          }}
        >
          {shown && shown.importable > 0 ? `Import ${formatNumber(shown.importable)}` : 'Import'}
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
