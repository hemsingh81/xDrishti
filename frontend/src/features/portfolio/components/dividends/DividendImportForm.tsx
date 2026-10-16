import { Alert, Button, Stack, Typography } from '@mui/material';
import { problemKind } from '../../lib/errors';
import { dividendErrorText } from '../../lib/dividendErrors';
import { uploadFieldText } from '../../lib/tradeErrors';
import { detectedKind, dividendImportKinds } from '../../lib/wrongFile';
import { WrongFileNotice } from '../WrongFileNotice';
import { FileDropZone } from '../trades/FileDropZone';

interface DividendImportFormProps {
  accountName: string;
  file: File | null;
  fileProblem: string | null;
  /** A preview or an import is running: nothing can be changed meanwhile. */
  busy: boolean;
  previewing: boolean;
  /** Why the preview failed, if it did. */
  error: unknown;
  onFile: (file: File) => void;
  onClear: () => void;
  onPreview: () => void;
  /** A tradebook was dropped here: opens the trade import. */
  onImportTrades: () => void;
  /** A report of Dhan was dropped here: opens Import files. */
  onImportFiles: () => void;
}

/** Step one: the file (drop zone or picker), then Preview. Nothing is stored by it. */
export function DividendImportForm({
  accountName,
  file,
  fileProblem,
  busy,
  previewing,
  error,
  onFile,
  onClear,
  onPreview,
  onImportTrades,
  onImportFiles,
}: DividendImportFormProps) {
  const detected = error ? detectedKind(error, dividendImportKinds) : null;
  const fields = error && problemKind(error) === 'invalid' ? uploadFieldText(error) : {};
  const fieldProblem = fields.file ?? fields.other;
  const general = error && detected === null && fieldProblem === undefined ? error : null;
  return (
    <Stack component="section" aria-label="Choose the file" spacing={2}>
      <Typography variant="body2" color="text.secondary">
        {`Load a dividend payout list for ${accountName}. Required columns: a date, the scrip name (or symbol or ISIN) and the amount paid; optional: dividend per share and quantity. Dates like 13 Jun 2024 or 2024-06-13. The file has no account column, so it goes to this account. Loading the same file again adds nothing.`}
      </Typography>
      <FileDropZone
        what="dividend list"
        file={file}
        onFile={onFile}
        onClear={onClear}
        disabled={busy}
        error={fileProblem ?? fieldProblem}
      />
      {detected !== null && (
        <WrongFileNotice
          detected={detected}
          onSwitch={detected === 'trades' ? onImportTrades : undefined}
          onImportFiles={onImportFiles}
        />
      )}
      {general ? (
        <Alert
          severity="error"
          role="alert"
          action={
            <Button color="inherit" size="small" disabled={busy || !file} onClick={onPreview}>
              Retry
            </Button>
          }
        >
          {dividendErrorText(general, 'The file could not be read.')}
        </Alert>
      ) : null}
      <Stack direction="row" sx={{ gap: 1, alignItems: 'center' }}>
        <Button variant="contained" disabled={!file || busy} loading={previewing} onClick={onPreview}>
          Preview
        </Button>
        <Typography variant="body2" color="text.secondary">
          Shows what importing would do. Nothing is stored yet.
        </Typography>
      </Stack>
    </Stack>
  );
}
